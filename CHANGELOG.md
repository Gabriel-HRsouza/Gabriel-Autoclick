# 📋 Histórico de Versões (Changelog)

Todas as mudanças notáveis do projeto **Gabriel Autoclick** estão documentadas neste arquivo.

---

## 🚀 [v1.1.0] - 2026-11-21
### ✨ Novidades
- **Modo Background Click (Clique em Segundo Plano)**:
  - Envie cliques diretamente para janelas específicas em segundo plano sem mover ou travar o cursor real do mouse.
  - Suporte a múltiplos pontos de clique em background (`BackgroundClickPoint`) e gerenciamento dedicado (`BackgroundClickService`).
  - Destaque visual da janela alvo (`TargetHighlightWindow`) para facilitar a identificação e mira da janela escolhida.
  - Novos atalhos globais dedicados para controle rápido do modo Background Click.
- **Download e Instalação Facilitada para Windows (10 / 11 x64)**:
  - **Executável Portátil Single-File (`GabrielAutoclick.exe`)**: Baixe e execute com 2 cliques sem precisar instalar o .NET 8.
  - **Pacote Portátil Completo (`GabrielAutoclick-win-x64.zip`)**: Inclui o executável, guia rápido `LEIA-ME.txt` e o instalador automático de atalhos.
  - **Instalador Rápido (`Instalar-GabrielAutoclick.bat`)**: Copia o aplicativo para `%LOCALAPPDATA%\Programs\GabrielAutoclick` e cria atalhos na **Área de Trabalho** e no **Menu Iniciar** automaticamente.
  - **Instalação em 1 Comando via PowerShell (`install.ps1`)**: Suporte a download e instalação instantânea pelo terminal do Windows.
  - **GitHub Actions Release Pipeline**: Compilação e publicação automática de versões no GitHub Releases a cada nova tag `v*`.

### 🔧 Melhorias
- Repositório limpo com `.gitignore` otimizado (remoção de arquivos temporários `bin/` e `obj/` do rastreamento Git).
- Verificação de integridade com arquivo `SHA256SUMS.txt` gerado automaticamente em cada build.

---

## 🎉 [v1.0.0] - 2026-11-21
### ✨ Lançamento Inicial
- **Modos de Posicionamento**:
  - **Modo Seguir Cursor** (`GetCursorPos` em tempo real sem travar o mouse).
  - **Posição Atual** e **Ponto Fixo na Tela** com mira interativa (`F7`).
- **Controle de Alta Precisão e CPS**:
  - Conversor de CPS em tempo real, configuração `HH:MM:SS:MS` e presets rápidos (`1`, `5`, `10`, `20`, `50`, `100 CPS`).
- **Burst Mode (Rajada de Cliques)** e **Randomização Humanizada** (variação de intervalo e Jitter em pixels).
- **Multi Point Clicker**: Sequenciador de múltiplos pontos na tela com loops configuráveis.
- **Macro Recorder & Player**: Gravação global via `WH_MOUSE_LL`, controle de velocidade (`0.5x` a `3.0x`) e importação/exportação JSON.
- **Safe Zones (Zonas Protegidas)** e **Restrição por Janela Ativa**.
- **Mini Mode (HUD Flutuante)** sempre visível (`Always on Top`).
- **Global Hotkeys (`F6`, `F7`, `F8`, `F9`) & Panic Button (`F12` / `ESC`)**.
- **Sistema de Perfis** (Blox Fruits, Minecraft PvP, Velocidade Máxima, Trabalho) e **Painel de Estatísticas**.
