#!/usr/bin/env bash
# Plays a real LAN match between separate headless processes on this machine (the M3 exit gate):
# one host and N-1 joiners, all scripted-AI bots talking UDP over loopback. Fails if any process
# fails, if their final state hashes differ, or if the host's replay does not reproduce the match.
#
# The input delay is fixed (INPUT_DELAY, default 2 ticks) so the result is the same on every machine.
#
# Usage: eng/lan-match.sh [players=4] [port=47600] [configuration=Release]
# Build first: dotnet build src/Ashfall.Headless -c Release
set -euo pipefail

PLAYERS="${1:-4}"
PORT="${2:-47600}"
CONFIG="${3:-Release}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HEADLESS="$ROOT/src/Ashfall.Headless/bin/$CONFIG/net10.0/ashfall-headless.dll"
OUT="${LAN_MATCH_OUT:-$ROOT/lan-match}"
rm -rf "$OUT"
mkdir -p "$OUT"

pids=()
dotnet "$HEADLESS" --host --humans "$PLAYERS" --port "$PORT" --name HOST --input-delay "${INPUT_DELAY:-2}" \
  --hash-out "$OUT/peer0.hash" --record "$OUT/match.ashreplay" > "$OUT/peer0.log" 2>&1 &
pids+=($!)
sleep 2
for ((i = 1; i < PLAYERS; i++)); do
  dotnet "$HEADLESS" --join "127.0.0.1:$PORT" --name "PEER$i" --hash-out "$OUT/peer$i.hash" > "$OUT/peer$i.log" 2>&1 &
  pids+=($!)
done

status=0
for i in "${!pids[@]}"; do
  if ! wait "${pids[$i]}"; then
    echo "peer $i failed:"
    cat "$OUT/peer$i.log"
    status=1
  fi
done

for ((i = 0; i < PLAYERS; i++)); do
  tail -n 1 "$OUT/peer$i.log"
done

[ "$status" -eq 0 ] || exit 1

distinct="$(cat "$OUT"/peer*.hash | sort -u | wc -l | tr -d ' ')"
if [ "$distinct" -ne 1 ]; then
  echo "DESYNC: peers finished with different states:"
  cat "$OUT"/peer*.hash
  exit 1
fi

echo "All $PLAYERS processes agree on $(cat "$OUT/peer0.hash")"
dotnet "$HEADLESS" --replay "$OUT/match.ashreplay"
