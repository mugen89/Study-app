using StudyApp.Domain.Models;

namespace StudyApp.Domain.Prompts;

/// <summary>
/// Built-in prompt templates (English + Bulgarian). They are copied into the database on first start;
/// the user can edit them and "reset to default" restores the text below. When an update improves a
/// built-in template, unmodified copies are refreshed automatically.
/// </summary>
public static class PromptDefaults
{
    public const string Flashcards = "flashcards";
    public const string MultipleChoice = "mcq";
    public const string Mixed = "mixed";
    public const string Weak = "weak";
    public const string Terms = "terms";
    public const string Notes = "notes";
    public const string Pack = "pack";
    public const string Fix = "fix";

    public static IReadOnlyList<PromptTemplate> All(string lang) => lang == "bg" ? Bulgarian : English;

    public static PromptTemplate? Find(string key, string lang) => All(lang).FirstOrDefault(t => t.Key == key);

    private static PromptTemplate T(string key, string lang, int order, string name, string description, string body) => new()
    {
        Key = key,
        Lang = lang,
        Name = name,
        Description = description,
        Body = body.Trim(),
        IsBuiltIn = true,
        SortOrder = order,
    };

    // ─────────────────────────────────────────────────────────── English

    private static readonly IReadOnlyList<PromptTemplate> English = new[]
    {
        T(Flashcards, "en", 1, "Flashcards", "Question-and-answer cards for spaced repetition.", """
You are an expert tutor helping me study {{subject}}.
Textbook: {{source}}

Create {{count}} flashcards that cover {{scope}}.
Focus especially on: {{focus}}

Difficulty: {{difficulty_rules}}

Flashcard rules:
- One fact or idea per card. The "front" is a clear question or term; the "back" is a short, precise answer (about 25 words at most).
- Cover the material evenly from beginning to end: key definitions, processes, numbers, causes and effects, comparisons.
- Add a "hint" that helps me remember without giving the answer away.
- No duplicates and no yes/no questions.

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_flashcards}}
"""),

        T(MultipleChoice, "en", 2, "Multiple-choice test", "Questions with one or several correct options.", """
You are an expert exam writer helping me study {{subject}}.
Textbook: {{source}}

Create a multiple-choice test with {{count}} questions that covers {{scope}}.
Focus especially on: {{focus}}

Difficulty: {{difficulty_rules}}

Question rules:
- Use mostly "single" questions (exactly one correct option out of 4) and some "multiple" questions (2–3 correct options out of 4–6).
- Spread the correct answers evenly across A, B, C and D – do not favour one letter.
- Wrong options must be plausible, of similar length to the correct one, and based on common mistakes. Avoid "all of the above" and "none of the above".
- Every question must be answerable from the textbook alone and test understanding, not trivia.
{{option_why_rule}}

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_test:single,multiple}}
"""),

        T(Mixed, "en", 3, "Mixed test", "Multiple choice, true/false and short-answer questions.", """
You are an expert exam writer helping me study {{subject}}.
Textbook: {{source}}

Create a practice test with {{count}} questions that covers {{scope}}.
Focus especially on: {{focus}}

Difficulty: {{difficulty_rules}}

Mix these question types (roughly equal numbers):
{{question_types}}

Question rules:
- Spread the correct answers of choice questions evenly across A, B, C and D.
- Wrong options must be plausible and based on common mistakes. Avoid "all of the above" / "none of the above".
- True/false statements must be clearly true or clearly false according to the textbook – no trick wording.
- Short-answer questions need a model "answer" and 2–4 "keyPoints" that a good answer must mention.
{{option_why_rule}}

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_test}}
"""),

        T(Weak, "en", 4, "Harder questions on my weak topics", "Targets the items you miss most – filled in automatically from your results.", """
You are an expert tutor helping me study {{subject}}.
Textbook: {{source}}

These are the items I keep getting wrong (with the textbook pages):
{{weak_items}}

Create {{count}} NEW questions that target exactly these weak concepts and are harder than the originals.
Difficulty: {{difficulty_rules}}

Rules for these questions:
- Approach each concept from a different angle: application to a new case, comparison with a similar concept, cause and effect, or a common misconception.
- Do not repeat the original questions word for word.
- Use these question types:
{{question_types}}
{{option_why_rule}}

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_test}}
"""),

        T(Terms, "en", 5, "Key terms & definitions", "Glossary flashcards for the most important terms.", """
You are an expert tutor helping me study {{subject}}.
Textbook: {{source}}

Create flashcards for the {{count}} most important key terms and concepts in {{scope}}.
Focus especially on: {{focus}}

Rules:
- "front": the term (or a short "What is …?" question). "back": a precise definition in the textbook's wording, about 30 words at most.
- In "explanation" give an example or explain how the term relates to other concepts.
- Add "term" to the "tags" together with the topic.
- Use difficulty {{difficulty}} unless a term is clearly easier or harder.

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_flashcards}}
"""),

        T(Notes, "en", 6, "Study notes", "A structured summary to read in the comfortable reader view.", """
You are an expert tutor helping me study {{subject}}.
Textbook: {{source}}

Write clear, well-structured study notes for {{scope}} that I can read to revise.
Focus especially on: {{focus}}

Rules for the notes:
- Split them into about {{count}} sections that follow the order of the textbook (one section per subheading or topic).
- Each section has a "heading", "content" in simple Markdown (short paragraphs, bullet lists with "- ", **bold** for key terms) and the "page" range it covers.
- Explain simply and clearly, but keep every important fact, number and definition. End each section with 2–3 bullet points under "**Key points**".
- Use ONLY the uploaded textbook/material. Write in {{language}}. Keep the JSON keys and the "type" values in English.
- Output ONLY one valid JSON code block, with no text before or after it. Use double quotes, no comments, no trailing commas, and escape quotes inside text.
- If everything does not fit in one reply, output fewer complete sections rather than cutting the JSON off.

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_notes}}
"""),

        T(Pack, "en", 7, "Chapter pack", "Notes, flashcards and a test for one chapter in a single reply.", """
You are an expert tutor helping me study {{subject}}.
Textbook: {{source}}

Create a complete study pack for {{scope}} in ONE JSON reply, with three decks:
1. "notes": about 6 sections of structured study notes (Markdown, key terms in **bold**).
2. "deck": about {{count}} flashcards (one fact per card, short answers, with hints).
3. "test": about {{count}} questions that mix these types:
{{question_types}}

Focus especially on: {{focus}}
Difficulty: {{difficulty_rules}}
{{option_why_rule}}

{{rules}}

Additional instructions: {{extra}}

Use exactly this JSON format:
{{schema_pack}}
"""),

        T(Fix, "en", 8, "Fix my JSON", "Asks the AI to correct a reply that failed to import.", """
The JSON you gave me could not be imported into my study app. The app reported:

{{error}}

Please send the COMPLETE JSON again, corrected, in exactly the same format.
- Output ONLY one valid JSON code block, with no text before or after it.
- Use double quotes, no comments, no trailing commas, and escape any quotes inside text.
- Do not leave anything out. If it is too long for one reply, send fewer complete items and I will ask for the rest.
"""),
    };

    // ─────────────────────────────────────────────────────────── Bulgarian

    private static readonly IReadOnlyList<PromptTemplate> Bulgarian = new[]
    {
        T(Flashcards, "bg", 1, "Флаш карти", "Карти с въпрос и отговор за повторение през интервали.", """
Ти си опитен преподавател и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Създай {{count}} флаш карти, които обхващат {{scope}}.
Наблегни особено на: {{focus}}

Трудност: {{difficulty_rules}}

Правила за картите:
- Един факт или една идея на карта. "front" е ясен въпрос или термин; "back" е кратък и точен отговор (до около 25 думи).
- Обхвани материала равномерно от началото до края: ключови определения, процеси, числа, причини и следствия, сравнения.
- Добави "hint" – подсказка, която помага да си спомня, без да издава отговора.
- Без повторения и без въпроси с отговор „да/не“.

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_flashcards}}
"""),

        T(MultipleChoice, "bg", 2, "Тест с избираем отговор", "Въпроси с един или няколко верни отговора.", """
Ти си опитен автор на изпитни тестове и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Създай тест с избираем отговор от {{count}} въпроса, който обхваща {{scope}}.
Наблегни особено на: {{focus}}

Трудност: {{difficulty_rules}}

Правила за въпросите:
- Използвай предимно въпроси "single" (точно един верен отговор от 4) и няколко въпроса "multiple" (2–3 верни отговора от 4–6).
- Разпредели верните отговори равномерно между A, B, C и D – не предпочитай една буква.
- Грешните отговори трябва да са правдоподобни, с дължина, близка до верния, и да отразяват чести грешки. Избягвай „всички изброени“ и „нито един от изброените“.
- На всеки въпрос трябва да може да се отговори само с помощта на учебника и той да проверява разбиране, а не дребни детайли.
{{option_why_rule}}

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_test:single,multiple}}
"""),

        T(Mixed, "bg", 3, "Смесен тест", "Избираем отговор, вярно/невярно и кратък отговор.", """
Ти си опитен автор на изпитни тестове и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Създай тест за упражнение от {{count}} въпроса, който обхваща {{scope}}.
Наблегни особено на: {{focus}}

Трудност: {{difficulty_rules}}

Смеси тези типове въпроси (в приблизително равен брой):
{{question_types}}

Правила за въпросите:
- Разпредели верните отговори на въпросите с избор равномерно между A, B, C и D.
- Грешните отговори трябва да са правдоподобни и да отразяват чести грешки. Избягвай „всички изброени“ / „нито един от изброените“.
- Твърденията за вярно/невярно трябва да са ясно верни или ясно неверни според учебника – без подвеждащи формулировки.
- Въпросите с кратък отговор имат примерен отговор "answer" и 2–4 "keyPoints", които добрият отговор трябва да спомене.
{{option_why_rule}}

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_test}}
"""),

        T(Weak, "bg", 4, "По-трудни въпроси за слабите ми места", "Насочени към въпросите, които грешиш най-често – попълва се автоматично.", """
Ти си опитен преподавател и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Това са въпросите, на които най-често греша (със страниците в учебника):
{{weak_items}}

Създай {{count}} НОВИ въпроса, насочени точно към тези слаби места и по-трудни от първоначалните.
Трудност: {{difficulty_rules}}

Правила за тези въпроси:
- Подходи към всяко понятие от друг ъгъл: приложение в нов случай, сравнение със сходно понятие, причина и следствие или често срещана заблуда.
- Не повтаряй първоначалните въпроси дума по дума.
- Използвай тези типове въпроси:
{{question_types}}
{{option_why_rule}}

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_test}}
"""),

        T(Terms, "bg", 5, "Ключови термини и определения", "Флаш карти-речник с най-важните термини.", """
Ти си опитен преподавател и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Създай флаш карти за {{count}}-те най-важни ключови термина и понятия в {{scope}}.
Наблегни особено на: {{focus}}

Правила:
- "front": терминът (или кратък въпрос „Какво е …?“). "back": точно определение с формулировката от учебника, до около 30 думи.
- В "explanation" дай пример или обясни как терминът е свързан с други понятия.
- Добави "term" в "tags" заедно с темата.
- Използвай трудност {{difficulty}}, освен ако даден термин е явно по-лесен или по-труден.

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_flashcards}}
"""),

        T(Notes, "bg", 6, "Учебни бележки", "Структурирано резюме за четене в удобния режим за четене.", """
Ти си опитен преподавател и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Напиши ясни, добре структурирани учебни бележки за {{scope}}, които да чета при преговор.
Наблегни особено на: {{focus}}

Правила за бележките:
- Раздели ги на около {{count}} раздела, които следват реда в учебника (един раздел за всяко подзаглавие или тема).
- Всеки раздел има "heading", "content" в прост Markdown (кратки абзаци, списъци с "- ", **удебелени** ключови термини) и "page" – страниците, които обхваща.
- Обяснявай просто и ясно, но запази всички важни факти, числа и определения. Завърши всеки раздел с 2–3 точки под „**Ключови моменти**“.
- Използвай САМО качения учебник/материал. Пиши на {{language}}. Ключовете в JSON и стойностите на "type" остави на английски.
- Върни САМО един валиден JSON блок с код, без текст преди или след него. Използвай двойни кавички, без коментари, без запетая след последния елемент и екранирай кавичките в текста.
- Ако всичко не се събира в един отговор, върни по-малко, но завършени раздели, вместо да прекъсваш JSON-а.

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_notes}}
"""),

        T(Pack, "bg", 7, "Пакет за глава", "Бележки, флаш карти и тест за една глава в един отговор.", """
Ти си опитен преподавател и ми помагаш да уча {{subject}}.
Учебник: {{source}}

Създай пълен учебен пакет за {{scope}} в ЕДИН JSON отговор, с три комплекта:
1. "notes": около 6 раздела учебни бележки (Markdown, ключовите термини с **удебелен** шрифт).
2. "deck": около {{count}} флаш карти (един факт на карта, кратки отговори, с подсказки).
3. "test": около {{count}} въпроса, които смесват тези типове:
{{question_types}}

Наблегни особено на: {{focus}}
Трудност: {{difficulty_rules}}
{{option_why_rule}}

{{rules}}

Допълнителни указания: {{extra}}

Използвай точно този JSON формат:
{{schema_pack}}
"""),

        T(Fix, "bg", 8, "Поправи моя JSON", "Моли AI да поправи отговор, който не се импортира.", """
JSON-ът, който ми даде, не можа да се импортира в приложението ми за учене. Приложението показа:

{{error}}

Моля, изпрати отново ЦЕЛИЯ JSON, поправен, в точно същия формат.
- Върни САМО един валиден JSON блок с код, без текст преди или след него.
- Използвай двойни кавички, без коментари, без запетая след последния елемент и екранирай кавичките в текста.
- Не пропускай нищо. Ако е твърде дълъг за един отговор, изпрати по-малко, но завършени елементи, а останалите ще поискам отделно.
"""),
    };
}
