#!/bin/bash
# Multi-turn flows: same conversation id for each step. Saves to test-logs/flow-<time>.txt
OUT="$(dirname "$0")/flow-$(date +%Y%m%d-%H%M%S).txt"
say(){ # conv prompt
  curl -s -m 120 -H "Content-Type: application/json" -d "{\"prompt\":\"$2\",\"conversationId\":\"$1\"}" http://localhost:5070/api/chat > /tmp/r.$$
  echo "USER: $2" | tee -a "$OUT"
  echo "BOT : $(grep -o '"delta":"[^"]*"' /tmp/r.$$ | sed 's/"delta":"//;s/"$//' | tr -d '\n' | cut -c1-260)   [reports=$(grep -c '"report"' /tmp/r.$$)]" | tee -a "$OUT"
  sleep 3
}
new(){ powershell -NoProfile -c "[guid]::NewGuid().ToString()" | tr -d '\r\n'; }
c=$(new); echo "--- flow 1: no period, then period" | tee -a "$OUT"
say $c "How much came from insurance versus patients"; say $c "May"
c=$(new); echo "--- flow 2: no period, split first, then period" | tee -a "$OUT"
say $c "How much did we collect"; say $c "by payer"; say $c "last quarter"
c=$(new); echo "--- flow 3: denials no period" | tee -a "$OUT"
say $c "How many claims were denied"; say $c "April"
c=$(new); echo "--- flow 4: A/R needs no period" | tee -a "$OUT"
say $c "How much is outstanding in A/R"
c=$(new); echo "--- flow 5: follow-up reuses period" | tee -a "$OUT"
say $c "Insurance versus patient payments in May"; say $c "same by payer"
echo "saved: $OUT"
