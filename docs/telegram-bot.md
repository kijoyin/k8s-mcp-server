# Telegram Bot Setup for Hermes Notifications

This guide walks through creating a Telegram bot and configuring Hermes to send messages.

## 1. Create a Telegram Bot

1. Open Telegram and search for **@BotFather**
2. Start a chat and send `/newbot`
3. Follow prompts:
   - **Name**: Your bot's display name (e.g., "K8s MCP Monitor")
   - **Username**: Must end in `bot` (e.g., `k8s_mcp_monitor_bot`)
4. BotFather will return a **bot token** — save it securely:
   ```
   123456789:ABCdefGhIJKlmNoPQRsTUVwxyZ
   ```

## 2. Get Your Chat ID

### Option A: Direct Message
1. Start a chat with your new bot
2. Send any message (e.g., "hello")
3. Visit: `https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getUpdates`
4. Find `"chat":{"id":123456789,...}` in the response

### Option B: Group/Channel
1. Add bot to group/channel
2. Send a message mentioning the bot: `@your_bot_name hello`
3. Check `getUpdates` as above — the chat ID will be negative for groups (e.g., `-1001234567890`)

## 3. Configure Hermes

### Add to Hermes config (`~/.hermes/config.yaml`):

```yaml
telegram:
  bot_token: "${TELEGRAM_BOT_TOKEN}"
  default_chat_id: "${TELEGRAM_CHAT_ID}"
  parse_mode: "MarkdownV2"  # or "HTML"
```

### Or use environment variables (`~/.hermes/.env`):

```bash
TELEGRAM_BOT_TOKEN=123456789:ABCdefGhIJKlmNoPQRsTUVwxyZ
TELEGRAM_CHAT_ID=123456789
```

## 4. Test the Integration

### Via Hermes CLI:
```bash
# Send test message
hermes telegram send --chat-id 123456789 --text "🧪 Test from Hermes"
```

### Via Python (standalone):
```python
import requests

bot_token = "YOUR_BOT_TOKEN"
chat_id = "YOUR_CHAT_ID"

response = requests.post(
    f"https://api.telegram.org/bot{bot_token}/sendMessage",
    json={
        "chat_id": chat_id,
        "text": "🧪 Test from K8s MCP Monitor",
        "parse_mode": "MarkdownV2"
    }
)
print(response.json())
```

## 5. MarkdownV2 Formatting Tips

Telegram's MarkdownV2 requires escaping special characters:

| Character | Escape |
|-----------|--------|
| `_` | `\_` |
| `*` | `\*` |
| `[` | `\[` |
| `]` | `\]` |
| `(` | `\(` |
| `)` | `\)` |
| `~` | `\~` |
| `` ` `` | `\`` |
| `>` | `\>` |
| `#` | `\#` |
| `+` | `\+` |
| `-` | `\-` |
| `=` | `\=` |
| `|` | `\|` |
| `{` | `\{` |
| `}` | `\}` |
| `.` | `\.` |
| `!` | `\!` |

**Helper function for Python:**
```python
def escape_markdown_v2(text: str) -> str:
    """Escape text for Telegram MarkdownV2."""
    special = r'_*[]()~`>#+-=|{}.!'
    return ''.join(f'\\{c}' if c in special else c for c in text)
```

## 6. Message Length Limits

- **Max message length**: 4096 characters
- **Long messages**: Split into multiple messages or use a file upload

## 7. Using with Hermes Cron Jobs

In your cron job YAML:

```yaml
deliver: "telegram:123456789"  # or "telegram:-1001234567890" for groups
```

The cron job's final response will be sent as a Telegram message.

## 8. Advanced: Inline Keyboards

For interactive messages, add `reply_markup`:

```json
{
  "chat_id": "123456789",
  "text": "Cluster alert: High memory usage",
  "reply_markup": {
    "inline_keyboard": [[
      {"text": "View in Grafana", "url": "https://grafana.example.com/d/..."},
      {"text": "Acknowledge", "callback_data": "ack_alert_123"}
    ]]
  }
}
```

## 9. Security Best Practices

- **Never commit bot tokens** to git — use environment variables
- **Restrict bot privacy** in BotFather: `/setprivacy` → `Disable` (allows reading all messages in groups)
- **Use chat ID allowlist** in your Hermes config to prevent unauthorized delivery
- **Rotate tokens periodically** via BotFather: `/revoke`

## 10. Troubleshooting

| Issue | Solution |
|-------|----------|
| `401 Unauthorized` | Bot token incorrect or revoked |
| `400 Bad Request: chat not found` | Chat ID wrong, or bot not in group/channel |
| `429 Too Many Requests` | Rate limited — add delays between messages |
| Messages not delivered | Check `deliver` field in cron job matches chat ID format |