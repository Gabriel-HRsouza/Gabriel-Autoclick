# ⚡ GABRIEL AUTOCLICK - Advanced Mouse Automation

[![Release](https://img.shields.io/github/v/release/Gabriel-HRsouza/Gabriel-Autoclick?color=00E5FF&label=Vers%C3%A3o%20Atual&style=for-the-badge)](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest)
[![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(64--bit)-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest)
[![.NET](https://img.shields.io/badge/.NET%208.0-Autocontido%20(N%C3%A3o%20precisa%20instalar)-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest)

> **Autoclicker profissional, completo e moderno para Windows 10 e Windows 11 (x64).**  
> Desenvolvido em **C# / WPF / .NET 8**, utilizando Windows API nativa (`SendInput`, `PostMessage`, `GetCursorPos`, Global Hotkeys, Low-Level Hooks).

---

## 📥 Download Rápido para Windows (10 e 11 - 64 bits)

Você **não precisa instalar o .NET nem o Visual Studio**. Escolha uma das opções abaixo para baixar e usar imediatamente:

| Opção | Link Direto | Descrição |
|---|---|---|
| 🚀 **1. Executável Direto (`.exe`)** *(Recomendado)* | **[⬇️ Baixar GabrielAutoclick.exe](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest/download/GabrielAutoclick.exe)** | **Portátil (Clique e Use):** Basta baixar e dar 2 cliques para abrir. |
| 📦 **2. Pacote Portátil (`.zip`)** | **[⬇️ Baixar GabrielAutoclick-win-x64.zip](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest/download/GabrielAutoclick-win-x64.zip)** | Contém o `.exe` + criador automático de atalho na Área de Trabalho + guia `LEIA-ME.txt`. |
| ⚡ **3. Instalador de Atalhos (`.bat`)** | **[⬇️ Baixar Instalar-GabrielAutoclick.bat](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest/download/Instalar-GabrielAutoclick.bat)** | Baixa a última versão automaticamente e cria atalho na **Área de Trabalho** e **Menu Iniciar**. |
| 🗂️ **Todas as Versões** | **[📋 Ver Releases no GitHub](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases)** | Histórico completo de versões (`v1.1.0`, `v1.0.0`, notas de atualização e hashes SHA-256). |

### 💻 Instalação Instantânea via PowerShell (1 Comando)
Se preferir instalar e criar os atalhos na Área de Trabalho com um único comando, abra o **PowerShell** do Windows e cole:
```powershell
irm https://raw.githubusercontent.com/Gabriel-HRsouza/Gabriel-Autoclick/main/install.ps1 | iex
```

> 💡 **Aviso do Windows SmartScreen:** Como o executável é independente e gratuito (sem certificado digital pago da Microsoft), na primeira abertura o Windows pode exibir a tela *"O Windows protegeu o seu computador"*. Basta clicar em **"Mais informações"** e depois em **"Executar assim mesmo"**.

---

## 🌟 Visão Geral

O **Gabriel Autoclick** foi projetado para superar autoclickers convencionais (como OP Auto Clicker e GS Auto Clicker), unindo **máxima precisão de cliques**, **modo de clique em segundo plano (Background Click)**, **design moderno glassmorphic** com papel de parede personalizado, e um conjunto avançado de funcionalidades para produtividade e jogos (Roblox, Blox Fruits, Minecraft PvP, MMORPGs, testes de software e automações corporativas).

---

## 🔥 Funcionalidades Principais

### 1. 🎯 Modos de Posicionamento do Clique
- **🔥 Modo Seguir Cursor (Diferencial Principal)**: O clique acompanha em tempo real o ponteiro do mouse sem reposicionamento artificial e sem travar o movimento. A cada iteração, `GetCursorPos` é consultado dinamicamente — mesmo com o aplicativo minimizado.
- **Posição Atual**: Captura a coordenada exata onde o mouse estava no instante em que o clique foi iniciado.
- **Ponto Fixo na Tela**: Permite digitar coordenadas `(X, Y)` ou capturá-las interativamente através da **Mira na Tela** (`F7`).

### 2. 👻 Background Click (Clique em Segundo Plano) *(Novo na v1.1.0)*
- Envia cliques diretamente para uma janela selecionada em segundo plano sem sequestrar nem mover o cursor real do seu mouse.
- Inclui **Destaque Visual da Janela Alvo (`TargetHighlightWindow`)** e suporte a múltiplos pontos de clique em background.

### 3. ⚡ Alta Precisão & Controle de Intervalo
- **Configuração Completa**: Horas, Minutos, Segundos e Milissegundos (`00:00:00:100`).
- **Conversor em Tempo Real de CPS (Clicks Per Second)**: Altere o CPS (ex: `10 CPS`, `20 CPS`) e o tempo em milissegundos é recalculado instantaneamente.
- **Presets Rápidos**: Botões de 1, 5, 10, 20, 50 e 100 CPS.
- **Proteção contra travamento**: Limite mínimo seguro configurável para evitar sobrecarga do sistema.

### 4. 💥 Burst Mode (Rajada de Cliques)
- Configure número de cliques por rajada (ex: `5 cliques`), intervalo entre cada clique (`50 ms`) e tempo de espera entre rajadas (`1000 ms`).

### 5. 🎲 Randomização Humanizada
- **Intervalo Aleatório**: Define limites mínimo e máximo (ex: `80 ms - 150 ms`) gerando intervalos imprevisíveis para evitar bloqueios anti-cheat.
- **Randomização de Posição (Jitter)**: Adiciona uma variação aleatória de pixels dentro de um raio configurável (ex: `±3 px`).

### 6. 🎯 Multi Point Clicker (Multi Click)
- Tabela interativa para sequenciar múltiplos pontos na tela com tempos de espera e botões específicos:
  $$\text{Ponto 1 } (X_1, Y_1) \longrightarrow \text{Ponto 2 } (X_2, Y_2) \longrightarrow \text{Ponto 3 } (X_3, Y_3) \dots$$
- Suporta repetição em loop contínuo ou número fixo de ciclos.

### 7. 🎬 Macro Recorder & Player
- Gravador global via `WH_MOUSE_LL` para registrar coordenadas, cliques, descidas/subidas de botão e tempos de espera.
- Reprodução com multiplicador de velocidade (`0.5x`, `1.0x`, `1.5x`, `2.0x`, `3.0x`).
- Exportação e importação de sequências em formato JSON.

### 8. 🛡️ Safe Zones (Zonas Protegidas)
- Crie zonas retangulares na tela onde cliques são proibidos (ex: botão de fechar aplicativo, barra de tarefas, chat).
- Desenhe as áreas diretamente sobre a tela através do seletor visual integrado.

### 9. 🪟 Restrição por Janela Ativa
- Escolha uma janela aberta (ex: `Roblox`, `Discord`, `Chrome`, `Notepad`).
- O Gabriel Autoclick só clica quando a janela alvo estiver focada no primeiro plano, pausando automaticamente ao trocar de janela.

### 10. 🗗 Mini Mode (HUD Flutuante)
- Modo compacto e flutuante contendo indicador de status, CPS atual e botão de Iniciar/Parar.
- Pode ser arrastado livremente e mantido sempre sobre outras telas (`Always on Top`).

### 11. ⌨️ Global Hotkeys & Panic Button
- Funciona globalmente mesmo com a janela minimizada ou em segundo plano:
  - **`F6`**: Iniciar / Parar Autoclicker
  - **`F7`**: Abrir Mira para Selecionar Ponto
  - **`F8`**: Iniciar Reprodução de Macro
  - **`F9`**: Parar Macro
  - **`F12` ou `ESC`**: 🚨 **PANIC BUTTON** — Interrompe imediatamente qualquer clique, macro, timer ou sequência ativa.

### 12. 📁 Sistema de Perfis
- Perfis prontos inclusos:
  - **Blox Fruits (Farm)**: 12 CPS com Follow Cursor
  - **Minecraft PvP**: Cliques rápidos com randomização de intervalo e jitter
  - **Velocidade Máxima (20 CPS)**
  - **Trabalho (Auto-Refresh)**: 1 clique a cada 5 segundos em ponto fixo
- Crie, duplique, salve, importe e exporte seus próprios perfis em JSON.

### 13. 📈 Painel de Estatísticas
- Contador de cliques na sessão e lifetime acumulado.
- Tempo ativo, CPS médio, maior CPS registrado e macros executadas.
- Persistência automática em `%APPDATA%\GabrielAutoclick\statistics.json`.

---

## 🏷️ Histórico de Versões (Releases)

Consulte o arquivo [CHANGELOG.md](CHANGELOG.md) ou a [página de Releases](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases) para baixar qualquer versão:

- **[`v1.1.0` (Mais Recente)](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/tag/v1.1.0)** — Novo modo **Background Click** (cliques em segundo plano em janelas específicas com destaque visual da janela alvo), instaladores automáticos para Windows (`Instalar-GabrielAutoclick.bat` e `install.ps1`), pacote portátil `.zip` e automação de releases via GitHub Actions.
- **[`v1.0.0`](https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/tag/v1.0.0)** — Lançamento inicial com Modo Seguir Cursor, Burst Mode, Randomização Humanizada, Multi Point Clicker, Gravador de Macros, Safe Zones, Restrição por Janela, Mini Mode HUD, Global Hotkeys + Panic Button, Perfis e Estatísticas.

---

## 📂 Estrutura do Projeto

```
GabrielAutoclick/
│
├── GabrielAutoclick.sln                # Solução do Visual Studio / .NET CLI
├── build-release.bat                   # Script automatizado para compilar o executável e pacote ZIP
├── build-release.ps1                   # Pipeline PowerShell de build Single-File + ZIP + SHA256
├── Instalar-GabrielAutoclick.bat       # Instalador rápido com criação de atalhos no Windows
├── install.ps1                         # Instalador de 1 linha via PowerShell
├── CHANGELOG.md                        # Histórico de versões detalhado
├── README.md                           # Documentação completa
│
├── .github/workflows/
│   └── release.yml                     # GitHub Actions para build e release automático por tag
│
└── GabrielAutoclick/
    ├── GabrielAutoclick.csproj         # Configuração do projeto WPF x64 .NET 8
    ├── App.xaml / App.xaml.cs          # Recursos, estilos glassmorphism e ciclo de vida
    ├── MainWindow.xaml(.cs)            # Interface principal moderna e cards
    ├── MainWindow.Background.cs        # Lógica de interface do modo Background Click
    ├── Models/                         # Modelos de dados (ClickSettings, BackgroundClickPoint, etc.)
    ├── Services/                       # Motores de clique, BackgroundClick, Macro, Hotkeys, TrayIcon
    ├── Helpers/                        # P/Invoke Win32 (NativeMethods) e Constantes
    ├── Views/                          # MiniWindow, PointPicker, SafeZonePicker, TargetHighlightWindow
    └── Assets/                         # Ícone e background de alta resolução
```

---

## 🛠️ Como Compilar Manualmente (Para Desenvolvedores)

### Pré-requisitos
- **Windows 10 ou Windows 11 (64-bit)**
- **.NET 8.0 SDK** (ou superior)

### Executar em modo de desenvolvimento:
```bash
dotnet restore
dotnet run --project GabrielAutoclick/GabrielAutoclick.csproj
```

### Gerar o Executável Single-File + Pacote `.ZIP` de Release:
Basta executar o script automatizado:
```cmd
build-release.bat
```
Os arquivos finais serão gerados na pasta `release\`:
- `release\GabrielAutoclick.exe` (Executável portátil Single-File x64)
- `release\GabrielAutoclick-win-x64.zip` (Pacote portátil com instalador de atalho e guia)
- `release\SHA256SUMS.txt` (Hashes SHA-256 de verificação)

---

## ⌨️ Tabela de Atalhos Padrão

| Atalho | Função | Descrição |
|---|---|---|
| **`F6`** | **Iniciar / Parar** | Alterna o estado do autoclicker principal |
| **`F7`** | **Selecionar Posição** | Abre a mira na tela para capturar coordenada `(X,Y)` |
| **`F8`** | **Iniciar Macro** | Inicia a reprodução da macro gravada |
| **`F9`** | **Parar Macro** | Interrompe a reprodução da macro |
| **`F12` / `ESC`** | **🚨 Panic Button** | Interrompe imediatamente qualquer clique, macro ou timer |

---

## 🔒 Diretórios de Configuração e Dados
O aplicativo salva automaticamente todas as configurações, perfis e estatísticas de forma limpa em:
- `%APPDATA%\GabrielAutoclick\settings.json`
- `%APPDATA%\GabrielAutoclick\profiles.json`
- `%APPDATA%\GabrielAutoclick\statistics.json`
- `%APPDATA%\GabrielAutoclick\macros\`

---

## 🛡️ Solução de Problemas (Troubleshooting)

1. **O clique não ocorre dentro de jogos que executam como Administrador:**
   - Clique com o botão direito no `GabrielAutoclick.exe` e escolha **Executar como Administrador** para que a API do Windows tenha permissão de enviar eventos para a janela elevada do jogo.
2. **O autoclicker pausou repentinamente:**
   - Verifique se a opção **Restrição por Janela** ou **Safe Zones** está ativa e se o mouse entrou em uma área protegida.
3. **Pressionei ESC e tudo parou:**
   - O botão `ESC` e a tecla `F12` são mapeados como Panic Button para segurança imediata.

---

**Desenvolvido com excelência para automação ágil e precisa no Windows.**
