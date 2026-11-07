#!/bin/bash
# Instala o Pastel para o seu usuário (nada de sistema, sem sudo).
# Uso: ./install.sh        (de dentro da pasta extraída do pacote)
set -e

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/pastel-app"
BIN="$HOME/.local/bin"

if [ ! -x "$SRC/pastel" ]; then
  echo "Não achei o programa 'pastel' ao lado deste script. Rode o install.sh de dentro da pasta do pacote." >&2
  exit 1
fi

mkdir -p "$DEST" "$BIN"
# fecha o Pastel que estiver rodando, para poder trocar os arquivos
pkill -u "$USER" -x pastel 2>/dev/null || true
sleep 0.5

rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$SRC"/. "$DEST"/
chmod +x "$DEST/pastel"
ln -sf "$DEST/pastel" "$BIN/pastel"

echo "Pastel instalado em $DEST"
case ":$PATH:" in
  *":$BIN:"*) ;;
  *) echo "Dica: $BIN não está no seu PATH. O Pastel funciona mesmo assim; só o comando 'pastel' no terminal não." ;;
esac

# Abre na bandeja. Na primeira vez ele cria sozinho a entrada do menu de aplicativos e o menu do botão direito.
setsid "$DEST/pastel" --background >/dev/null 2>&1 &
echo "Pronto. Procure o ícone do Pastel na bandeja, ou clique com o botão direito num arquivo no Dolphin."
