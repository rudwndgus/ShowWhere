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
  claimed: boolean;
  desktop?: WebSocket;
  mobile?: WebSocket;
}

const safeEqual = (left: string, right: string): boolean => {
  const a = Buffer.from(left);
  const b = Buffer.from(right);
  return a.length === b.length && timingSafeEqual(a, b);
};

export class PairingManager {
  private readonly sessions = new Map<string, PairingSession>();
  private readonly sockets = new WebSocketServer({ noServer: true, maxPayload: 32_768 });

  constructor(private readonly ttlMs = 60_000) {
    this.sockets.on('connection', (socket, request) => this.acceptSocket(socket, request));
  }

  create(publicBaseUrl: string) {
    this.prune();
    const session: PairingSession = {
      id: randomBytes(16).toString('hex'),
      pairingToken: randomBytes(24).toString('base64url'),
      desktopSecret: randomBytes(32).toString('base64url'),
      code: randomInt(0, 1_000_000).toString().padStart(6, '0'),
      expiresAt: Date.now() + this.ttlMs,
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
    if (!session || session.claimed || session.expiresAt <= Date.now() || !safeEqual(session.code, code)) return null;
    session.claimed = true;
    session.mobileSecret = randomBytes(32).toString('base64url');
    return { sessionId: session.id, mobileSecret: session.mobileSecret };
  }

  disconnect(sessionId: string): void {
    const session = this.sessions.get(sessionId);
    if (!session) return;
    session.desktop?.close(1000, 'desktop_disconnected');
    session.mobile?.close(1000, 'desktop_disconnected');
    this.sessions.delete(sessionId);
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
    for (const session of this.sessions.values()) this.disconnect(session.id);
    this.sockets.close();
  }

  private acceptSocket(socket: WebSocket, request: IncomingMessage): void {
    const url = new URL(request.url ?? '/', 'http://localhost');
    const session = this.sessions.get(url.searchParams.get('sessionId') ?? '');
    const role = url.searchParams.get('role');
    const secret = url.searchParams.get('secret') ?? '';
    if (!session || (role === 'desktop' && !safeEqual(secret, session.desktopSecret))
      || (role === 'mobile' && (!session.mobileSecret || !safeEqual(secret, session.mobileSecret)))
      || (role !== 'desktop' && role !== 'mobile')) {
      socket.close(1008, 'invalid_session');
      return;
    }
    const current = role === 'desktop' ? session.desktop : session.mobile;
    if (current?.readyState === WebSocket.OPEN) {
      socket.close(1008, 'peer_already_connected');
      return;
    }
    if (role === 'desktop') session.desktop = socket;
    else session.mobile = socket;
    this.notifyStatus(session);

    socket.on('message', (data, binary) => {
      if (binary) return;
      let message: unknown;
      try { message = JSON.parse(data.toString()); } catch { return; }
      if (!this.validMessage(message, role)) return;
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
      && typeof item.id === 'string' && typeof item.text === 'string'
      && item.text.trim().length > 0 && item.text.length <= 2_000;
    return item.type === 'chat_message' && typeof item.id === 'string'
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

  private prune(): void {
    const now = Date.now();
    for (const [id, session] of this.sessions)
      if (!session.claimed && session.expiresAt <= now) this.disconnect(id);
  }
}
