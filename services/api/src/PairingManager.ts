import { randomBytes, randomInt, timingSafeEqual } from 'node:crypto';
import type { IncomingMessage } from 'node:http';
import type { Duplex } from 'node:stream';
import { WebSocket, WebSocketServer } from 'ws';

interface PairingSession {
  id: string;
  pairingToken: string;
  code: string;
  desktopSecret: string;
  mobileSecret?: string;
  expiresAt: number;
  absoluteExpiresAt: number;
  failedClaims: number;
  mobileMessageWindowStartedAt: number;
  mobileMessageCount: number;
  claimed: boolean;
  desktop?: WebSocket;
  mobile?: WebSocket;
}

const safeEqual = (left: string, right: string): boolean => {
  const a = Buffer.from(left);
  const b = Buffer.from(right);
  return a.length === b.length && timingSafeEqual(a, b);
};

const MAX_FAILED_CLAIMS = 5;
const MAX_ACTIVE_SESSIONS = 1_000;
const MAX_SESSION_LIFETIME_MS = 30 * 60_000;
const HEARTBEAT_INTERVAL_MS = 30_000;

export class PairingManager {
  private readonly sessions = new Map<string, PairingSession>();
  private readonly sockets = new WebSocketServer({
    noServer: true,
    maxPayload: 32_768,
    handleProtocols: (protocols) => protocols.has('showwhere-v1') ? 'showwhere-v1' : false,
  });
  private readonly liveSockets = new WeakSet<WebSocket>();
  private readonly heartbeat: NodeJS.Timeout;

  constructor(private readonly ttlMs = 60_000) {
    this.sockets.on('connection', (socket, request) => this.acceptSocket(socket, request));
    this.heartbeat = setInterval(() => this.runMaintenance(), HEARTBEAT_INTERVAL_MS);
    this.heartbeat.unref();
  }

  create(publicBaseUrl: string) {
    this.prune();
    if (this.sessions.size >= MAX_ACTIVE_SESSIONS) return null;
    const now = Date.now();
    const session: PairingSession = {
      id: randomBytes(16).toString('hex'),
      pairingToken: randomBytes(24).toString('base64url'),
      desktopSecret: randomBytes(32).toString('base64url'),
      code: randomInt(0, 1_000_000).toString().padStart(6, '0'),
      expiresAt: now + this.ttlMs,
      absoluteExpiresAt: now + MAX_SESSION_LIFETIME_MS,
      failedClaims: 0,
      mobileMessageWindowStartedAt: now,
      mobileMessageCount: 0,
      claimed: false,
    };
    this.sessions.set(session.id, session);
    const mobileUrl = `${publicBaseUrl}/mobile/?pair=${encodeURIComponent(session.pairingToken)}`;
    return {
      sessionId: session.id,
      desktopSecret: session.desktopSecret,
      pairingToken: session.pairingToken,
      code: session.code,
      expiresAt: new Date(session.expiresAt).toISOString(),
      mobileUrl,
    };
  }

  claim(pairingToken: string, code: string) {
    this.prune();
    const session = [...this.sessions.values()].find((item) => safeEqual(item.pairingToken, pairingToken));
    if (!session || session.claimed || session.expiresAt <= Date.now()) return null;
    if (!safeEqual(session.code, code)) {
      session.failedClaims += 1;
      if (session.failedClaims >= MAX_FAILED_CLAIMS) this.remove(session, 'too_many_attempts');
      return null;
    }
    session.claimed = true;
    session.mobileSecret = randomBytes(32).toString('base64url');
    return { sessionId: session.id, mobileSecret: session.mobileSecret };
  }

  disconnect(sessionId: string, desktopSecret: string): boolean {
    const session = this.sessions.get(sessionId);
    if (!session || !safeEqual(session.desktopSecret, desktopSecret)) return false;
    this.remove(session, 'desktop_disconnected');
    return true;
  }

  handleUpgrade(request: IncomingMessage, socket: Duplex, head: Buffer): boolean {
    const url = new URL(request.url ?? '/', 'http://localhost');
    if (url.pathname !== '/api/pairing/ws') return false;
    this.sockets.handleUpgrade(request, socket, head, (webSocket) => {
      this.sockets.emit('connection', webSocket, request);
    });
    return true;
  }

  close(): void {
    clearInterval(this.heartbeat);
    for (const session of [...this.sessions.values()]) this.remove(session, 'server_shutdown');
    this.sockets.close();
  }

  private acceptSocket(socket: WebSocket, request: IncomingMessage): void {
    this.prune();
    const url = new URL(request.url ?? '/', 'http://localhost');
    const session = this.sessions.get(url.searchParams.get('sessionId') ?? '');
    const role = url.searchParams.get('role');
    const protocols = (request.headers['sec-websocket-protocol'] ?? '')
      .split(',').map((value) => value.trim()).filter(Boolean);
    // Query secrets remain accepted for older desktop builds during the upgrade window.
    const secret = protocols[0] === 'showwhere-v1' ? protocols[1] ?? '' : url.searchParams.get('secret') ?? '';
    if (!session || session.absoluteExpiresAt <= Date.now()
      || (role === 'desktop' && !safeEqual(secret, session.desktopSecret))
      || (role === 'mobile' && (!session.mobileSecret || !safeEqual(secret, session.mobileSecret)))
      || (role !== 'desktop' && role !== 'mobile')) {
      socket.close(1008, 'invalid_session');
      return;
    }
    const current = role === 'desktop' ? session.desktop : session.mobile;
    if (current?.readyState === WebSocket.OPEN || current?.readyState === WebSocket.CONNECTING) {
      socket.close(1008, 'peer_already_connected');
      return;
    }
    if (role === 'desktop') session.desktop = socket;
    else session.mobile = socket;
    this.liveSockets.add(socket);
    socket.on('pong', () => this.liveSockets.add(socket));
    this.notifyStatus(session);

    socket.on('message', (data, binary) => {
      if (binary) return;
      let message: unknown;
      try { message = JSON.parse(data.toString()); } catch { return; }
      if (!this.validMessage(message, role)) return;
      if (role === 'mobile' && !this.allowMobileMessage(session)) {
        socket.close(1008, 'message_rate_limited');
        return;
      }
      const peer = role === 'desktop' ? session.mobile : session.desktop;
      if (peer?.readyState === WebSocket.OPEN) peer.send(JSON.stringify(message));
    });
    socket.on('close', () => {
      if (role === 'desktop' && session.desktop === socket) session.desktop = undefined;
      if (role === 'mobile' && session.mobile === socket) session.mobile = undefined;
      this.notifyStatus(session);
      if (session.claimed && !session.desktop && !session.mobile) this.sessions.delete(session.id);
    });
    socket.on('error', () => undefined);
  }

  private validMessage(value: unknown, role: string): boolean {
    if (!value || typeof value !== 'object') return false;
    const item = value as Record<string, unknown>;
    if (role === 'mobile') return item.type === 'user_message'
      && typeof item.id === 'string' && item.id.length > 0 && item.id.length <= 200
      && typeof item.text === 'string'
      && item.text.trim().length > 0 && item.text.length <= 2_000;
    return item.type === 'chat_message' && typeof item.id === 'string'
      && item.id.length > 0 && item.id.length <= 200
      && (item.role === 'user' || item.role === 'assistant')
      && typeof item.text === 'string' && item.text.length <= 8_000
      && typeof item.isPending === 'boolean';
  }

  private notifyStatus(session: PairingSession): void {
    const status = JSON.stringify({
      type: 'connection_status',
      connected: session.desktop?.readyState === WebSocket.OPEN && session.mobile?.readyState === WebSocket.OPEN,
    });
    for (const peer of [session.desktop, session.mobile])
      if (peer?.readyState === WebSocket.OPEN) peer.send(status);
  }

  private allowMobileMessage(session: PairingSession): boolean {
    const now = Date.now();
    if (now - session.mobileMessageWindowStartedAt >= 60_000) {
      session.mobileMessageWindowStartedAt = now;
      session.mobileMessageCount = 1;
      return true;
    }
    session.mobileMessageCount += 1;
    return session.mobileMessageCount <= 60;
  }

  private runMaintenance(): void {
    this.prune();
    for (const socket of this.sockets.clients) {
      if (!this.liveSockets.has(socket)) {
        socket.terminate();
        continue;
      }
      this.liveSockets.delete(socket);
      try { socket.ping(); } catch { socket.terminate(); }
    }
  }

  private prune(): void {
    const now = Date.now();
    for (const session of [...this.sessions.values()])
      if (session.absoluteExpiresAt <= now || (!session.claimed && session.expiresAt <= now))
        this.remove(session, 'session_expired');
  }

  private remove(session: PairingSession, reason: string): void {
    if (!this.sessions.delete(session.id)) return;
    for (const socket of [session.desktop, session.mobile]) {
      if (socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING)
        socket.close(1000, reason);
    }
  }
}
