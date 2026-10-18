#!/usr/bin/env bash
# Recompiles the renderer's Slang shaders to the SPIR-V embedded in Ashfall.Rendering.
#
# Building the game never needs this: the .spv files are committed. Run it after editing a .slang file,
# with slangc (https://github.com/shader-slang/slang/releases, 2026.12 or later) on PATH or in $SLANGC.
set -euo pipefail

SLANGC="${SLANGC:-slangc}"
SHADERS="$(cd "$(dirname "${BASH_SOURCE[0]}")/../src/Ashfall.Rendering/Shaders" && pwd)"

compile() {
    local source="$1" entry="$2" stage="$3" output="$4"
    "$SLANGC" "$SHADERS/$source" -target spirv -profile spirv_1_6 -entry "$entry" -stage "$stage" -o "$SHADERS/$output"
    echo "  $source:$entry -> $output"
}

echo "Compiling shaders in $SHADERS"
compile Scene.slang vertexMain vertex Scene.vert.spv
compile Scene.slang fragmentMain fragment Scene.frag.spv

if command -v spirv-val > /dev/null; then
    for spv in "$SHADERS"/*.spv; do
        spirv-val --target-env vulkan1.3 "$spv"
    done
    echo "spirv-val: all modules valid for Vulkan 1.3"
fi
