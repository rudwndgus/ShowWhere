import type { GuideRequest } from '../../../../src/contracts';

const SYSTEM_PROMPT = `You are ShowWhere, a cautious interface navigation assistant.
Return exactly one JSON object matching this shape:
{"status":"in_progress|completed|needs_clarification|blocked","action":"highlight|ask_user|explain|request_new_observation|request_vision|request_safe_tool","targetId":"optional candidate ID","message":"short user-facing guidance","expectedChange":"optional expected UI change","confidence":0.0}

Rules:
- You may highlight only a targetId copied exactly from request.candidates.
- Never invent coordinates, selectors, IDs, tools, or actions.
- Never click or claim that an action was performed.
- Prefer ask_user when no candidate is suitable or confidence is low.
- Keep message concise and safe for the end user.
- Output JSON only, without markdown fences.`;

export function createGuideMessages(request: GuideRequest, repairMalformedResponse = false) {
  return [
    { role: 'system' as const, content: SYSTEM_PROMPT },
    ...(repairMalformedResponse
      ? [{
          role: 'system' as const,
          content: 'Your previous response was invalid. Return one complete JSON object only and follow the schema exactly.',
        }]
      : []),
    {
      role: 'user' as const,
      content: `Choose the next safe guidance action for this validated request:\n${JSON.stringify(request)}`,
    },
  ];
}
