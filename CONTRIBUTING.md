# Como contribuir com o Pastel

Obrigado por querer ajudar! Leia o [Código de Conduta](CODE_OF_CONDUCT.md) antes.

## Regras gerais
- A branch **`main` é protegida**. Ninguém (nem o mantenedor) envia direto para ela: tudo entra por **Pull Request**.
- Contribuidores externos trabalham em **fork**; não há permissão de escrita no repositório principal.
- Toda PR precisa de **aprovação do mantenedor** e de **todas as conversas resolvidas**.
- O histórico da `main` é **linear** (usamos *squash* ou *rebase*, sem merge commits).
- Force-push e exclusão da `main` são proibidos.

## Antes de começar
1. Para mudanças grandes, **abra uma issue primeiro** e espere um retorno. Evita trabalho perdido.
2. Bugs pequenos e correções de texto podem ir direto para PR.
3. Vulnerabilidades: veja [SECURITY.md](SECURITY.md). Não use issue pública.

## Fluxo
1. Faça um fork e crie uma branch a partir da `main`: `fix/balao-fecha-sozinho`, `feat/arrastar-pasta`, `docs/readme`.
2. Faça a mudança, compile e **teste na prática** (Windows e/ou Linux KDE, diga qual).
3. Abra a PR para `main` preenchendo o modelo.
4. Responda aos comentários com novos commits. Mantenha a branch atualizada com a `main`.

## Commits
Mensagens curtas, no imperativo, em português, com prefixo:

```
feat: arrasta atalho para dentro da pasta
fix: balão não fecha ao clicar fora
docs: explica instalação no Linux
refactor: / chore: / build:
```

- Um assunto por commit; sem commits "wip" ou "ajustes".
- Não faça commit de `bin/`, `obj/`, `publish/`, instaladores, segredos ou chaves.
- Não force-push depois que a PR tiver revisão.

## Pull Requests
- Pequenas e com **um único objetivo**.
- Título no mesmo formato dos commits.
- Explique o *porquê*, não só o *o quê*; inclua capturas de tela se mudar a interface.
- PRs gigantes, sem relação com o projeto ou geradas sem revisão humana podem ser fechadas.

## Código
- Siga o estilo do código ao redor (C#/.NET no Windows, `linux/` para KDE).
- Não adicione dependências sem discutir antes em uma issue.
- Sem telemetria nem coleta de dados: o Pastel funciona offline e sem conta. A única conexão de rede é a procura por versões novas no GitHub (`Updater.cs`), que o usuário pode desligar. Não adicione outras.

## Licença
Ao contribuir, você concorda que sua contribuição será licenciada sob a [MIT](LICENSE).
