#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
CONFIGURATION="${CONFIGURATION:-Release}"
TARGET_FRAMEWORK="net10.0"
EXTENSIONS_DIR="$REPO_ROOT/Extensions"
OUT_DIR="$REPO_ROOT/Server/bin/$CONFIGURATION/$TARGET_FRAMEWORK/Extensions"

# Extension build order (lower dependency first)
EXTENSION_ORDER=(
    "Chat"
    "Trade"
)

restore_extensions() {
    for name in "${EXTENSION_ORDER[@]}"; do
        local proj="$EXTENSIONS_DIR/$name/Server/${name}Extension.Server.csproj"
        if [[ -f "$proj" ]]; then
            echo "[RESTORE] $name..."
            dotnet restore "$proj" > /dev/null
        fi
    done
}

build_extension() {
    local name="$1"
    local proj="$EXTENSIONS_DIR/$name/Server/${name}Extension.Server.csproj"
    local contracts_proj="$EXTENSIONS_DIR/$name/Contracts/${name}Extension.csproj"

    if [[ ! -f "$proj" ]]; then
        echo "[ERROR] Extension '$name' project not found: $proj"
        return 1
    fi

    echo "[BUILD] $name (Server)..."
    dotnet build "$proj" \
        -c "$CONFIGURATION" \
        --no-restore \
        /p:TargetFramework="$TARGET_FRAMEWORK"

    mkdir -p "$OUT_DIR"

    # Copy Server extension DLL
    local dll_src="$EXTENSIONS_DIR/$name/Server/bin/$CONFIGURATION/$TARGET_FRAMEWORK/${name}Extension.Server.dll"
    cp "$dll_src" "$OUT_DIR/"

    # Copy Contracts DLL (required at runtime for protobuf types etc.)
    if [[ -f "$contracts_proj" ]]; then
        local contracts_dll_src="$EXTENSIONS_DIR/$name/Contracts/bin/$CONFIGURATION/$TARGET_FRAMEWORK/${name}Extension.dll"
        if [[ -f "$contracts_dll_src" ]]; then
            cp "$contracts_dll_src" "$OUT_DIR/"
        fi
    fi

    echo "[OK] $name -> $OUT_DIR/"
}

clean_extensions() {
    echo "[CLEAN] Removing extension outputs..."
    rm -rf "$OUT_DIR"
    mkdir -p "$OUT_DIR"
    echo "[OK] Cleaned."
}

# --- main ---
cd "$REPO_ROOT"

case "${1:-}" in
    --restore)
        restore_extensions
        exit 0
        ;;
    --clean)
        clean_extensions
        exit 0
        ;;
esac

restore_extensions

if [[ -n "${1:-}" ]] && [[ "$1" != "--all" ]]; then
    build_extension "$1"
else
    for name in "${EXTENSION_ORDER[@]}"; do
        build_extension "$name"
    done
fi

echo ""
echo "=== All extensions deployed ==="
