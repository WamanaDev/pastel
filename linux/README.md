# Pastel para Linux (KDE Plasma)

A pasta do celular, no seu PC. Este é o Pastel para Linux, feito para o **KDE Plasma**. É um app separado do
Pastel do Windows (que é WPF), com o mesmo jeito de funcionar e a mesma cara, feito em .NET + Avalonia.

## Instalar

Baixe o pacote, extraia e rode:

```
./install.sh
```

Ele copia o Pastel para `~/.local/share/pastel-app`, abre na bandeja e cria sozinho a entrada no menu de
aplicativos e o menu do botão direito. Não usa `sudo` e não mexe em nada do sistema. Para remover: `./uninstall.sh` (os atalhos das pastas voltam para a área de trabalho e as pastas e os dados do Pastel são apagados).

**Atualização:** de tempos em tempos o Pastel consulta a página de versões do GitHub, avisa (notificação e item no menu da bandeja) e, se você aceitar, baixa o pacote, confere o SHA-256 e roda o `install.sh` dele. É a única conexão de rede do app e dá para desligar em Configurações > Geral.

Gerar o pacote você mesmo (precisa do SDK do .NET 10):

```
cd linux
dotnet publish -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

## O que faz

- **Pastas**: cada pasta é um atalho `.desktop` na sua área de trabalho, com um ícone gerado (grade 2x2 dos
  apps de dentro). O Plasma cuida de posição, grade, renomear e excluir; o Pastel acompanha.
- **Duplo clique** na pasta abre um balão com a ponta apontando para o ícone. Mais de 12 apps vira páginas
  (roda do mouse, setas ou pontinhos).
- **Soltar atalhos** da área de trabalho na pasta (ou no balão): só o atalho `.desktop` **sai** de lá e vai para
  `~/.local/share/pastel/Grupos/`. Remover da pasta ou excluí-la devolve o atalho.
- **Arquivo, programa ou pasta que não é atalho não é movido**: a pasta do Pastel recebe um atalho `.desktop`
  apontando para ele e o original **fica onde está** (ao soltar, e também no menu **Pastel** do Dolphin).
  Remover da pasta apaga só o atalho; o original nunca é mexido.
- **Ocultar pastas vazias** (opcional, em Configurações > Geral): a pasta vazia (que não acabou de ser criada) sai da
  área de trabalho e fica guardada em `~/.local/share/pastel/Ocultas/`; ela volta sozinha quando recebe um item.
  A pasta recém-criada fica visível até o primeiro item. Funciona mesmo com "mostrar arquivos ocultos" ligado.
- **Ícones ajustados**: ícones pequenos ou com muita margem (PNG de 48 px, glifo pequeno no meio de um fundo liso)
  são ampliados para preencher o quadrado arredondado, e o Pastel prefere a versão maior (SVG ou PNG grande) do tema;
  ícones que já enchem o quadrado ficam como estão.
- **Flutuantes**: barras de apps na área de trabalho, no fundo das janelas, que encaixam na grade. Tamanho
  mínimo a grande, com vidro ou sem fundo, com ou sem nome, e efeito do mouse (nenhum, destaque ou
  "quanto mais perto, maior", como um dock).
- **Botão direito → Pastel** (Dolphin e arquivos na área de trabalho): enviar para uma pasta ou flutuante, ou
  criar uma nova. Vem todo mundo de uma vez (vários arquivos selecionados).
- **Configurações** (clique no ícone da bandeja): pastas, flutuantes, tema claro/escuro, cor de destaque,
  animações, iniciar com a sessão e os menus.
- Segue o tema e a cor de destaque do KDE (lidos de `kdeglobals`) e refaz os ícones quando você os troca.

Configuração: `~/.config/pastel/config.json`. Apps movidos e ícones: `~/.local/share/pastel/`.

## O que foi verificado, e o que não foi

Escrito e testado numa máquina **Windows**, com um Ubuntu 24.04 no WSL2. **Nunca rodou num KDE de verdade.**

Provado num Linux real (Ubuntu), com `pastel --selftest` e `pastel --selftest-gui`:

- criar, renomear por fora e apagar por fora o atalho de uma pasta; mover e devolver atalhos;
- abrir um `.desktop` com `gio launch`, inclusive com espaços e aspas no caminho do `Exec`;
- o socket entre instâncias (segunda instância entrega os argumentos e sai);
- procurar ícones nos temas do sistema (PNG e SVG) e padronizá-los;
- o ajuste de ícones pequenos/com margem (ícone sintético pequeno enche o quadrado; ícone cheio fica igual) e a
  busca de uma versão maior no tema: este teste também passou no Windows;
- gerar o menu de contexto, a entrada do menu de aplicativos e o autostart;
- abrir o Avalonia num X11: o balão e a barra flutuante viram janelas reais com transparência de verdade
  (32 bits), no tamanho certo, a barra exatamente na célula da grade, e o pedido de "no fundo, fora da
  barra de tarefas" gravado na janela.

**Funções novas (0.1.3): só no autoteste.** Mover só atalhos `.desktop` (e criar atalho para arquivo, programa ou
pasta sem mover o original), ocultar pastas vazias e o ajuste de ícones têm casos no `pastel --selftest`, mas
**ninguém as viu funcionando num KDE real**. Em especial: o menu Pastel do Dolphin com esses itens, o soltar
do Dolphin/Plasma, e a pasta vazia sumir e voltar da área de trabalho do Plasma. Esta nota será ajustada
quando houver teste num KDE de verdade.

**Não verificado** (precisa de um KDE de verdade):

- se o KWin aplica o "no fundo" e o "fora da barra de tarefas" (o gerenciador de janelas do WSLg não aplica);
- o menu **Pastel** no botão direito do Dolphin (o arquivo é gerado no formato certo, mas ninguém o abriu);
- o ícone na bandeja (precisa de D-Bus; sem ele o app segue funcionando só pelas pastas);
- arrastar e soltar entre o Dolphin/Plasma e o balão;
- se os ícones novos aparecem na hora na área de trabalho do Plasma;
- se o Plasma remove e recoloca a pasta oculta sem deixar espaço vazio na grade ou reposicionar os outros ícones.

## Limitações do KDE e do Wayland

- **A grade do Plasma não pode ser lida.** O tamanho da célula dos ícones não é informado a outros
  programas. O Pastel usa 96 × 104 e você ajusta em **Configurações > Flutuantes** até a barra alinhar
  com os ícones.
- **Sessão Wayland**: o Pastel roda pelo XWayland. Os balões e flutuantes funcionam, mas o Wayland puro não
  informa onde está o mouse fora das janelas do app: sem isso o balão abre no meio da tela, e não junto do
  ícone. Na sessão X11 ele abre junto do ícone.
- **Menu do botão direito na área de trabalho vazia** ("Nova pasta" ali): o Plasma não tem um jeito padrão
  de adicionar itens a esse menu. Use o ícone da bandeja ou o botão direito no ícone do Pastel no menu de
  aplicativos (Nova pasta, Novo flutuante, Configurações).
- **Recentes do Menu Iniciar**: os atalhos das pastas ficam na área de trabalho, fora das pastas de
  aplicativos, então não aparecem como apps. Não confirmei se o Kickoff lista o "Pastel" em recentes.
- **Pastas ocultas** ficam em `~/.local/share/pastel/Ocultas/`, não na lixeira; se você apagar essa pasta, as
  pastas vazias somem de vez. A pasta oculta não aparece no Plasma até receber um item (ou até você desligar a opção).
- **Arquivos, programas e pastas** que não são atalhos não são movidos (por segurança): o que entra na pasta do Pastel
  é um atalho, então apagar o original por fora deixa um atalho quebrado dentro da pasta.
- Ícones muito pequenos só melhoram até onde o tema tem uma versão maior; se só existe um PNG minúsculo, ele é
  ampliado e pode ficar um pouco macio.
- Escolher arquivos usa `kdialog` (KDE) ou `zenity`. Sem nenhum dos dois, arraste os apps para a pasta.

## Para quem for mexer no código

- `Core/` não depende de tela: regras (`Library`), `.desktop`, menus, X11, socket.
- `Icons/`: procura e padroniza os ícones (SkiaSharp).
- `UI/`: balão, flutuante, configurações, diálogos.
- `Harness/`: desenha as janelas em memória (sem tela) e salva imagens: `dotnet run --project Harness`.
- Rodar os autotestes sem encostar na sua pasta pessoal (usam uma pasta de teste):

```
PASTEL_SANDBOX=/tmp/pastel-teste ./pastel --selftest
PASTEL_SANDBOX=/tmp/pastel-teste ./pastel --selftest-gui     # precisa de um X11
```

- O SkiaSharp tem que estar na mesma versão no código e no binário nativo de Linux (ver o `.csproj`);
  senão o app aborta ao desenhar o primeiro ícone.

Feito por Wictor Pamplona.
