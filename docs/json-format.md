# Study App JSON format (schemaVersion 1)

This is the format the AI prompt templates ask ChatGPT/Claude to produce, and the format **Export** writes.
A formal JSON Schema is in [`studyapp.schema.json`](studyapp.schema.json); a complete example (English and
Bulgarian) is in [`/samples`](../samples).

The importer is deliberately **forgiving**: it accepts many alternative field names and repairs common LLM
mistakes (see the end of this page). Invalid items are skipped with a readable message — the rest still imports.

## One deck per file (what the prompts produce)

```json
{
  "schemaVersion": 1,
  "subject": "Human Anatomy",
  "source": "Gray's Anatomy for Students, 4th ed.",
  "chapter": { "number": "3", "title": "The Skeletal System", "pages": "45-78" },
  "deck": { "title": "Skull & spine", "kind": "test", "difficulty": 3 },
  "items": [ … ]
}
```

| Field | Meaning |
|---|---|
| `schemaVersion` | Always `1` for now. |
| `subject` | Subject (course/textbook) name. Used to suggest or match the target subject. |
| `source` | Textbook name. Shown with every item so you know where to re-read. |
| `chapter` | `number`, `title`, `pages`. Matched to an existing chapter by number or title, or created. A plain string like `"Chapter 3: Bones"` also works. |
| `deck` | `title`, `kind` (`deck` = flashcards, `test` = questions, `notes` = reading notes), `difficulty` (1–5), `description`, `language`. All optional. |
| `items` | The study items (below). |

## Several decks / chapters in one file (what Export writes)

```json
{
  "schemaVersion": 1,
  "subject": "Human Anatomy",
  "decks": [
    { "title": "Chapter 3 – notes", "kind": "notes", "chapter": { "number": "3", "title": "Bones" }, "items": [ … ] },
    { "title": "Chapter 3 – flashcards", "kind": "deck", "chapter": { "number": "3", "title": "Bones" }, "items": [ … ] }
  ]
}
```

`"chapters": [ { "number": "1", "title": "…", "items": [ … ] } ]` is accepted as well.

## Item types

Every item may have these common fields:

| Field | Meaning |
|---|---|
| `type` | `flashcard`, `single`, `multiple`, `truefalse`, `short`, `note` |
| `page` | Page(s) in the textbook: `"52"`, `"52-53"`, `52`, `["52", "60"]` |
| `explanation` | Why the answer is right (shown after answering) |
| `difficulty` | `1`–`5` or `"easy" / "medium" / "hard" / "expert"` |
| `tags` | `["topic", "subtopic"]` or `"topic, subtopic"` |
| `hint` | Small hint (flashcards) |
| `source` | Overrides the textbook name for this item |

### Flashcard
```json
{ "type": "flashcard", "front": "What is the heart's pacemaker?", "back": "The sinoatrial (SA) node.",
  "hint": "In the right atrium", "explanation": "…", "page": "16", "difficulty": 2, "tags": ["heartbeat"] }
```

### Single choice (one correct option)
```json
{ "type": "single", "question": "Which vessel carries oxygen-poor blood to the lungs?",
  "options": ["Aorta", "Pulmonary artery", "Pulmonary vein", "Vena cava"],
  "answer": "B", "explanation": "…", "page": "14" }
```
Options may be objects with an explanation per option (shown after answering):
```json
"options": [ { "text": "Aorta", "why": "The aorta carries oxygen-rich blood." }, { "text": "Pulmonary artery", "why": "Correct – …" } ]
```

### Multiple choice (several correct options)
```json
{ "type": "multiple", "question": "Which are heart valves?", "options": ["Tricuspid", "Mitral", "Sinoatrial", "Aortic"],
  "answer": ["A", "B", "D"], "explanation": "…", "page": "13" }
```

### True / false
```json
{ "type": "truefalse", "question": "The right ventricle pumps blood to the whole body.", "answer": false, "explanation": "…", "page": "15" }
```

### Short answer (self-graded against a model answer)
```json
{ "type": "short", "question": "What is the contraction phase called?", "answer": "Systole",
  "accept": ["ventricular systole"], "keyPoints": ["the contraction phase"], "explanation": "…", "page": "17" }
```
The app suggests a grade by comparing the typed answer with `answer`/`accept`; the learner always confirms it
(Missed / Partly / Got it).

### Note (study notes, shown in the reader)
```json
{ "type": "note", "heading": "Structure of the heart", "content": "Markdown: paragraphs, - bullets, **bold**", "page": "10-12" }
```

## Accepted variations (LLM quirks the importer handles)

- The whole chat reply can be pasted: text before/after the JSON and ```` ```json ```` fences are removed.
- Trailing commas, `// comments`, raw line breaks inside strings, missing commas between objects, typographic quotes.
- **Cut-off replies**: all complete items are recovered; a warning suggests asking the AI to continue.
- Answers as letters (`"B"`, `"b"`, `"B)"`, `"Option B"`, `["A","C"]`, `"A, C"`, Cyrillic `"Б"`), as the option text,
  or as a number (0-based; flagged for checking). Options like `"A) text"` lose their letter prefix.
- `"correct": true` flags on option objects; per-option explanations as `why`/`explanation`/`feedback`.
- Alternative names: `question/prompt/q`, `front/term`, `back/definition`, `answer/correctAnswer/correct_answer`,
  `explanation/rationale/reason`, `page/pages/pageRef/page_reference`, `items/flashcards/cards/questions/quiz/notes/sections`…
- Type names such as `mcq`, `multiple_choice`, `true_false`, `tf`, `open`, `essay`, `fill_in_the_blank`, `glossary`.
- A bare array `[ … ]` of items; types are inferred from the fields.

## Duplicates

Each item gets a fingerprint (type group + normalised question text). When importing into a subject that already
contains the same item, you choose: **skip** (default), **replace** (update the text, keep study progress) or **keep both**.
