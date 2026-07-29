import type { CandidateChoice } from '../types/search';

interface CandidateChoicesProps {
  choices: CandidateChoice[];
  onSelect: (choice: CandidateChoice) => void;
}

export function CandidateChoices({ choices, onSelect }: CandidateChoicesProps) {
  if (choices.length === 0) return null;
  return (
    <div className="sw-choices" aria-label="검색 후보 선택">
      {choices.slice(0, 3).map((choice) => (
        <button
          key={choice.id}
          type="button"
          onClick={() => onSelect(choice)}
        >
          {choice.label}
        </button>
      ))}
    </div>
  );
}
