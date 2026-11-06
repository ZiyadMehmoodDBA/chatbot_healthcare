#!/bin/bash
# Sends each question to POST /api/chat (new conversation each) and saves raw NDJSON + summary.txt.
# Data in the dev DB ends 2026-06-05, so date-bound questions name a month (no 4-digit year:
# the external-input screen blocks any 4+ digit number).
# Usage: bash test-logs/run-tests.sh   (API must be running on :5070)
OUT="$(dirname "$0")/run-$(date +%Y%m%d-%H%M%S)"; mkdir -p "$OUT"
Q=(
"How much did we collect in May?"
"How much came from insurance versus patients in May?"
"Which payers pay us the most in May?"
"How much is outstanding in A/R?"
"How old is our A/R?"
"How much A/R is over 90 days?"
"How many claims were denied in May?"
"Why are claims being denied in May?"
"How many claims were resubmitted in May?"
"How many claims did we process in May?"
"Which providers bill the most in May?"
"How long does it take us to bill in May?"
"How much did we bill in May?"
"What is our net collection rate?"
"What is our 30-day readmission rate?"
"How much came from insurance versus patients"
)
i=0
for q in "${Q[@]}"; do
  i=$((i+1))
  id=$(powershell -NoProfile -c "[guid]::NewGuid().ToString()" | tr -d '\r\n')
  f="$OUT/q$(printf %02d $i).ndjson"
  body=$(printf '{"prompt":"%s","conversationId":"%s"}' "$q" "$id")
  code=$(curl -s -m 120 -o "$f" -w "%{http_code}" -H "Content-Type: application/json" -d "$body" http://localhost:5070/api/chat)
  rep=$(grep -c '"report"' "$f"); err=$(grep -c '"error"' "$f")
  msg=$(grep -o '"delta":"[^"]*"' "$f" | head -1 | cut -c1-110)
  printf 'Q%02d http=%s reports=%s errors=%s | %s\n      %s\n' $i "$code" "$rep" "$err" "$q" "$msg" | tee -a "$OUT/summary.txt"
  sleep 4
done
echo "saved: $OUT"
