#!/bin/bash
# Remove o Pastel e tudo o que ele criou. Antes disso, os atalhos que estavam nas pastas e flutuantes
# voltam para a área de trabalho, e as pastas do Pastel e as configurações são apagadas.
# Uso: ./uninstall.sh        (-y para não perguntar)
set -e

DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
CONF="${XDG_CONFIG_HOME:-$HOME/.config}"
APP="$DATA/pastel-app"

if [ "$1" != "-y" ] && [ -t 0 ]; then
  echo "Ao desinstalar, os atalhos das suas pastas e flutuantes voltam para a área de trabalho,"
  echo "e as pastas do Pastel e as configurações são apagadas."
  read -r -p "Desinstalar? [s/N] " ans
  case "$ans" in s|S|y|Y) ;; *) echo "Cancelado."; exit 0 ;; esac
fi

pkill -u "$USER" -x pastel 2>/dev/null || true
sleep 0.5

# com o Pastel fechado, para ele não regravar nada
if [ -x "$APP/pastel" ]; then
  "$APP/pastel" --uninstall-cleanup || echo "Aviso: não consegui devolver todos os atalhos; veja $DATA/pastel/Grupos." >&2
fi

rm -rf "$APP"
rm -f "$HOME/.local/bin/pastel"
rm -f "$DATA/applications/pastel.desktop"
rm -f "$DATA/kio/servicemenus/pastel-enviar.desktop"
rm -f "$CONF/autostart/pastel.desktop"

echo "Pastel removido."
