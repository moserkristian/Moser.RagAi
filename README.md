# Document desk

Staff upload policies, SOPs, and short contracts and ask questions. The desk answers from those files and shows the passage it used. If the files do not contain the answer, it says so.

The files in `samples/` are fictional and marked as samples.

## Run

You need the .NET 10 SDK and Docker. Docker runs PostgreSQL with pgvector. A Gemini API key is only needed for generated answers. Search still runs without one.

Copy `.env.example` to `.env` and set the values. Do not commit `.env`. The database password and the desk password are read from the environment, not from a file in git. Avoid `;` in the database password because it breaks the connection string.

```powershell
Copy-Item .env.example .env
docker compose up -d db
$env:ConnectionStrings__Desk = "Host=localhost;Port=5432;Database=desk;Username=desk;Password=YOUR_PASSWORD"
$env:Desk__Users__0__UserName = "staff"
$env:Desk__Users__0__Password = "YOUR_PASSWORD"
$env:GEMINI_API_KEY = "YOUR_KEY"
dotnet run --project src/Moser.RagAi.Web
```

The site listens on http://localhost:5080. Sign in, upload a file from `samples/`, and ask a question. Leave both sign-in fields blank to continue as Anonymous. A user name or a password is checked against the configured account.

The first start downloads a local embedding model into `models/` (about 23 MB). That folder is not committed.

To run the site and the database together:

```powershell
docker compose up --build
```

The site then listens on http://localhost:8080. `GET /health` checks Postgres and the embedding files.

```powershell
dotnet test
```

## Answers

Retrieval uses a local MiniLM model (`Xenova/all-MiniLM-L6-v2`, 384 dimensions, Apache-2.0). It does not call a paid embeddings API.

Answers are generated with `gemini-3.6-flash` on the Gemini API. The key is `GEMINI_API_KEY`.

Llama 3.3 70B is no longer on Groq's free tier. Groq removed `llama-3.3-70b-versatile` for free and developer accounts on 16 August 2026. Groq's current free replacement, `openai/gpt-oss-120b`, is limited to 30 requests per minute, 1,000 requests per day, 8,000 tokens per minute, and 200,000 tokens per day. Eight thousand tokens a minute is tight once a prompt includes several passages.

`gemini-3.8-flash` is listed, and a GET for that model returns HTTP 200. `generateContent` for `gemini-3.8-flash` and `gemini-3.7-flash` sends no response headers. On 17 October 2026 a short request with thinking set to low waited 120 seconds and received 0 bytes. The same request to `gemini-3.6-flash` returned HTTP 200. The pricing page lists standard input and output for `gemini-3.5-flash` as free of charge, and Google routes those calls to `gemini-3.6-flash`. Free-tier prompts may be used by Google to improve its products. Google does not publish a fixed requests-per-minute number. The limit is per project and is shown in AI Studio. Specified limits are not guaranteed. When the API returns 429, the desk says the model is rate limited and does not invent an answer.

The model is told to use only the numbered passages. Those passages are sent as data, separate from the instructions. If retrieval finds nothing close enough, the model is not called.

## What it stores

Documents and chunks are in PostgreSQL. Chunk embeddings use pgvector. Uploads are limited to 8 MB and to PDF, text, and Markdown. A PDF with no text layer is rejected. The desk does not OCR scans.

## Layout

The screens follow a free Magnific vector by pikisuperstar (resource 17723042). The stock files are not in this repo. Markup and CSS here are written for this app. Nunito is under the SIL Open Font License (`src/Moser.RagAi.Web/wwwroot/fonts/OFL.txt`).
