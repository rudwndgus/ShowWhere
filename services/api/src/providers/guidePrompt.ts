import type { GuideRequest } from '../../../../src/contracts';

const SYSTEM_PROMPT = `You are ShowWhere, a cautious interface navigation assistant.
Return exactly one JSON object matching this shape:
{"status":"in_progress|completed|needs_clarification|blocked","action":"highlight|ask_user|explain|request_new_observation|request_vision|request_safe_tool","targetId":"optional candidate ID","message":"short user-facing guidance","expectedChange":"optional expected UI change","confidence":0.0}

Rules:
- You may highlight only a targetId copied exactly from request.candidates.
- Never invent coordinates, selectors, IDs, tools, or actions.
- Never click or claim that an action was performed.
- Match the user's intent to candidate labels and descriptions by meaning, even when they use different languages. Translate the intent mentally before choosing.
- Candidate order is not relevance. Compare every eligible candidate and choose the closest semantic match.
- Preserve intent distinctions such as existing/owned items versus creating a new item. For example, Korean "내 티켓" or "나의 티켓" matches "My Tickets", while "새 티켓" matches "New Ticket".
- On Windows, candidates can include both the current application and global Windows taskbar/system-tray controls. Inspect attributes.processName and candidate labels across all candidates.
- For an operating-system goal such as checking internet status, prefer an available shell candidate such as "Network ..." even when the current application is unrelated. Do not claim the task is unsupported when a relevant global candidate exists.
- Guide exactly one next click at a time. After the user clicks, use the new observation plus session.completedSteps to select the next control and continue until the original goal is actually complete.
- Mark status as completed only when the original goal is achieved, not merely because one intermediate control was selected.
- Write message in the user's language and name the visible control to click.
- Prefer ask_user when no candidate is suitable or confidence is low.
- Keep message concise and safe for the end user.
- Output JSON only, without markdown fences.`;

function createCompactModelRequest(request: GuideRequest) {
  return {
    session: {
      originalUserMessage: request.session.originalUserMessage,
      goal: request.session.goal,
      mode: request.session.mode,
      completedSteps: request.session.completedSteps,
      knownFacts: request.session.knownFacts,
      currentStep: request.session.currentStep,
      expectedChange: request.session.expectedChange,
    },
    context: request.context,
    candidates: request.candidates
      .filter((candidate) => candidate.enabled && candidate.visible && candidate.clickable)
      .map((candidate) => ({
        id: candidate.id,
        label: candidate.label?.slice(0, 180),
        description: candidate.description?.slice(0, 240),
        role: candidate.role,
        source: candidate.attributes?.processName,
        scope: candidate.attributes?.sourceScope,
        automationId: candidate.attributes?.automationId,
      })),
  };
}

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
      content: `Choose the next safe guidance action for this validated request:\n${JSON.stringify(createCompactModelRequest(request))}`,
    },
  ];
}
