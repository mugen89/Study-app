# Study App – user guide

*Български: [USER-GUIDE.bg.md](USER-GUIDE.bg.md)*

Study App turns your textbook into flashcards, tests and study notes with the help of ChatGPT or Claude,
and lets you learn them with spaced repetition, practice tests and a comfortable reader.

## 1. Install

1. Open the [Releases page](https://github.com/mugen89/Study-app/releases/latest) and download `StudyApp-…-win-x64.zip`.
2. Unzip it into a folder where you can save files, for example **Documents\StudyApp** (not *Program Files*).
3. Double-click **StudyApp.exe**. Tip: right-click it → *Pin to Start* or *Send to → Desktop (create shortcut)*.

> The first time, Windows may say *"Windows protected your PC"*, because the app is not code-signed.
> Click **More info → Run anyway**. This only happens once.

Everything you study is saved in the **data** folder next to `StudyApp.exe`. To move to another computer,
copy the whole StudyApp folder.

## 2. The idea in three steps

1. **Create a subject** (one per course or textbook). Add chapters if you like.
2. **Ask your AI for study material.** In ChatGPT or Claude, upload your textbook (a *Project* is ideal so it stays
   available). In Study App open **AI prompts**, choose a template, fill in subject, chapter and pages,
   click **Copy prompt** and paste it into the chat.
3. **Import the reply.** Copy the AI's answer, open **Import** and click **Paste from clipboard**
   (or drag a saved `.json` file onto the window). Check the preview, choose the subject and chapter, click **Import**.

Want to try first? On the Home page click **Try the sample subject**.

## 3. AI prompt templates

| Template | What you get |
|---|---|
| Flashcards | Question/answer cards with hints, explanations and page numbers |
| Multiple-choice test | Questions with one or several correct options, with a "why" for each option |
| Mixed test | Multiple choice, true/false and short-answer questions |
| Harder questions on my weak topics | New, harder questions about the items **you** miss most — filled in automatically |
| Key terms & definitions | Glossary flashcards |
| Study notes | A structured summary to read in the reader |
| Chapter pack | Notes + flashcards + a test for one chapter in one reply |
| Fix my JSON | If an import fails, this asks the AI to correct its reply |

Fill-in fields: subject, textbook, chapter, pages, number of items, difficulty (1–5 or mixed), question types,
the language of the answer (e.g. *same as the textbook* or *Bulgarian*), focus topics and extra instructions.
Every prompt asks the AI for **page numbers** and an **explanation** for each item, and to answer with JSON only.

You can edit any template (pencil button). **Reset to default** brings back the original text.

**Tips**
- Ask for one chapter at a time and 20–40 items per reply.
- If a long reply is cut off, Study App still imports every complete item. Ask the AI to *"continue"* and import the rest.
- If an import fails, use **Copy a "fix it" prompt** on the Import page and paste it into the chat.

## 4. Studying

**Study** (on a subject, chapter or deck) starts a *smart review*: items that are due today plus a few new ones.
Use **⋯ → Study with options…** to choose exactly what to study (only missed, only flagged, only new, difficulty,
topics, order, how many, time limit).

Flashcards: press **Space** to turn the card, then rate yourself:

| Key | Rating | Meaning |
|---|---|---|
| 1 | Again | I didn't know it – show it again soon |
| 2 | Hard | I knew it, but it was difficult |
| 3 | Good | I knew it |
| 4 | Easy | Too easy – show it much later |

The app schedules the next review accordingly (the button shows when). Other keys: **H** hint, **F** flag for later,
**N** personal note, **E** edit, **W** mark "wrong in source", **Ctrl+Z** undo, **Esc** end.
Study sessions hide the sidebar so you can focus.

## 5. Tests

**Test** opens the test options: question types, which questions, difficulty, topics, order, number of questions,
an optional **time limit**, and whether to see the answers **after each question** or **at the end (exam mode)**.
Choose answers with the mouse or keys **1–9 / A–H** (true/false: **T / F**). Written answers are compared with the
model answer and **you** decide: *Missed it / Partly / Got it*.

The results page shows your score, time, **the pages to re-read**, and every missed item with the correct answer
and explanation. From there: **Review missed now**, **Ask AI to explain** (copies a ready prompt), or
**Get harder questions on these**.

## 6. Reading notes

Notes decks open in the reader: a contents list on the left, the text on the right, **Mark as read** per section.
Use **Ctrl +/–** or the zoom buttons for the text size and the **Aa** button for theme, line spacing and width.

## 7. When the AI made a mistake

- Press **E** (or click an item anywhere) to correct it.
- Press **W** to mark it **wrong in source** — it is left out of study until you fix it (Review → *Wrong in source*).
- Press **N** to add a personal note that shows up every time you study the item.

## 8. Review, search and the recycle bin

- **Review**: everything due today, recently missed, flagged, and items marked wrong in source.
- **Search** (**Ctrl+K**): find any card, question or note in all subjects.
- Deleted things go to the **Recycle bin** for 30 days (and every delete has an **Undo** button).

## 9. Settings that make reading comfortable

Settings → **Appearance**: English/Български, light/dark/**sepia** theme, accent colour, size of the app.
Settings → **Reading**: font, sizes for flashcards, questions, answers and notes, **line spacing** and **text width**,
with a live preview.
Settings → **Study**: daily goal, new items per day, how strictly typed answers are checked, and more.

## 10. Backups, updates and feedback

- A **backup** of your data is made automatically every day and before each update
  (Settings → Data & backups → restore any backup).
- **Updates**: when a new version exists, a button appears at the bottom of the sidebar. The update keeps all your data.
- **Feedback**: Settings → About & feedback. Describe the problem or idea and send it via GitHub, or copy it into an email.

## Keyboard shortcuts

Press **F1** in the app for the full list. The most useful: **Ctrl+1/2/3** Home/Subjects/Review, **Ctrl+K** search,
**Ctrl+I** import, **Ctrl+P** prompts, **Ctrl+T** light/dark, **Alt+←** back.
