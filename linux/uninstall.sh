#!/bin/bash
# Remove o Pastel e os menus que ele criou. NÃO apaga as suas pastas: os atalhos que saíram da
# área de trabalho continuam em ~/.local/share/pastel/Grupos (devolva-os antes, se quiser).
# Uso: ./uninstall.sh
set -e

DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
CONF="${XDG_CONFIG_HOME:-$HOME/.config}"

pkill -u "$USER" -x pastel 2>/dev/null || true
rm -rf "$DATA/pastel-app"
rm -f "$HOME/.local/bin/pastel"
rm -f "$DATA/applications/pastel.desktop"
rm -f "$DATA/kio/servicemenus/pastel-enviar.desktop"
rm -f "$CONF/autostart/pastel.desktop"

echo "Pastel removido."
echo "Seus dados continuam em: $DATA/pastel (apps movidos) e $CONF/pastel (configuração)."
echo "Os atalhos das pastas na área de trabalho ficaram lá e não abrem mais: apague-os se não quiser."
