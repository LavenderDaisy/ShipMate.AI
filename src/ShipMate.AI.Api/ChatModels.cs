using Microsoft.SemanticKernel.ChatCompletion;

namespace ShipMate.AI.Api;

/// <summary>Request body for POST /api/chat.</summary>
public sealed record ChatRequest(string? SessionId, string? Message);

/// <summary>Response body for POST /api/chat.</summary>
public sealed record ChatResponse(string Reply);

/// <summary>
/// In-memory per-session chat history store. ConcurrentDictionary is fine for a demo;
/// a real app would use Redis or a database.
/// </summary>
public static class ChatSessionStore
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChatHistory> Histories = new();
}

/// <summary>The embedded HTML chat page served at GET /.</summary>
public static class ChatPage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ShipMate AI</title>
<style>
  :root { --bg:#0f1117; --card:#1a1d27; --accent:#4f9cf9; --text:#e4e6eb; --muted:#8b8f9a; }
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,-apple-system,sans-serif; background:var(--bg); color:var(--text); display:flex; flex-direction:column; height:100vh; }
  header { background:var(--card); padding:16px 24px; border-bottom:1px solid #2a2d3a; display:flex; align-items:center; gap:12px; }
  header .logo { width:36px; height:36px; background:var(--accent); border-radius:8px; display:flex; align-items:center; justify-content:center; font-size:20px; }
  header h1 { font-size:18px; font-weight:600; }
  header .sub { color:var(--muted); font-size:13px; margin-left:8px; }
  #chat { flex:1; overflow-y:auto; padding:24px; display:flex; flex-direction:column; gap:16px; }
  .msg { max-width:720px; padding:14px 18px; border-radius:12px; line-height:1.6; white-space:pre-wrap; word-wrap:break-word; }
  .user { background:var(--accent); color:#fff; align-self:flex-end; }
  .bot { background:var(--card); align-self:flex-start; border:1px solid #2a2d3a; }
  .bot.error { border-color:#e74c3c; color:#e74c3c; }
  .typing { color:var(--muted); font-style:italic; }
  #input-bar { background:var(--card); border-top:1px solid #2a2d3a; padding:16px 24px; display:flex; gap:12px; }
  #msg-input { flex:1; background:#0f1117; border:1px solid #2a2d3a; border-radius:8px; padding:12px 16px; color:var(--text); font-size:15px; outline:none; }
  #msg-input:focus { border-color:var(--accent); }
  #send-btn { background:var(--accent); color:#fff; border:none; border-radius:8px; padding:12px 24px; font-size:15px; cursor:pointer; font-weight:600; }
  #send-btn:hover { opacity:0.9; }
  #send-btn:disabled { opacity:0.5; cursor:not-allowed; }
  .examples { display:flex; gap:8px; flex-wrap:wrap; padding:0 24px 12px; background:var(--card); border-top:1px solid #2a2d3a; }
  .examples button { background:#2a2d3a; border:none; border-radius:6px; padding:6px 12px; color:var(--muted); font-size:13px; cursor:pointer; }
  .examples button:hover { color:var(--text); }
</style>
</head>
<body>
<header>
  <div class="logo">🚢</div>
  <h1>ShipMate AI</h1>
  <span class="sub">Conversational multi-carrier shipping copilot</span>
</header>
<div id="chat"></div>
<div class="examples">
  <button onclick="fill(this)">Cheapest overnight 30301→10001, 5 lb residential?</button>
  <button onclick="fill(this)">Ship it and print the label</button>
  <button onclick="fill(this)">Where is my package?</button>
</div>
<div id="input-bar">
  <input id="msg-input" placeholder="Ask about shipping rates, book a shipment, track a package..." autocomplete="off" />
  <button id="send-btn" onclick="send()">Send</button>
</div>
<script>
const sid = Math.random().toString(36).slice(2);
const chat = document.getElementById('chat');
const input = document.getElementById('msg-input');
const btn = document.getElementById('send-btn');

function add(text, cls) {
  const d = document.createElement('div');
  d.className = 'msg ' + cls;
  d.textContent = text;
  chat.appendChild(d);
  chat.scrollTop = chat.scrollHeight;
  return d;
}

function fill(b) { input.value = b.textContent; input.focus(); }

async function send() {
  const msg = input.value.trim();
  if (!msg) return;
  add(msg, 'user');
  input.value = '';
  btn.disabled = true;
  const typing = add('ShipMate is thinking…', 'bot typing');
  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionId: sid, message: msg })
    });
    const data = await res.json();
    typing.remove();
    add(data.reply, data.reply.startsWith('[error]') ? 'bot error' : 'bot');
  } catch(e) {
    typing.remove();
    add('Network error: ' + e.message, 'bot error');
  } finally {
    btn.disabled = false;
    input.focus();
  }
}

input.addEventListener('keydown', e => { if (e.key === 'Enter') send(); });
input.focus();
</script>
</body>
</html>
""";
}
