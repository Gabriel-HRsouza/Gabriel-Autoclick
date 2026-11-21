#  GABRIEL AUTOCLICK - Advanced Mouse Automation

> **Autoclicker profissional, completo e moderno para Windows 10 e Windows 11 (x64).**  
> Desenvolvido em **C# / WPF / .NET 8**, utilizando Windows API nativa (`SendInput`, `GetCursorPos`, Global Hotkeys, Low-Level Hooks).

---

##  Visão Geral

O **Gabriel Autoclick** foi projetado para superar autoclickers convencionais (como OP Auto Clicker e GS Auto Clicker), unindo **máxima precisão de cliques**, **design moderno glassmorphic** com papel de parede personalizado, e um conjunto avançado de funcionalidades para produtividade e jogos (Roblox, Blox Fruits, Minecraft PvP, MMORPGs, testes de software e automações corporativas).

---

##  Funcionalidades Principais

### 1.  Modos de Posicionamento do Clique
- ** Modo Seguir Cursor (Diferencial Principal)**: O clique acompanha em tempo real o ponteiro do mouse sem reposicionamento artificial e sem travar o movimento. A cada iteração, `GetCursorPos` é consultado dinamicamente — mesmo com o aplicativo minimizado.
- **Posição Atual**: Captura a coordenada exata onde o mouse estava no instante em que o clique foi iniciado.
- **Ponto Fixo na Tela**: Permite digitar coordenadas `(X, Y)` ou capturá-las interativamente através da **Mira na Tela** (`F7`).

### 2.  Alta Precisão & Controle de Intervalo
- **Configuração Completa**: Horas, Minutos, Segundos e Milissegundos (`00:00:00:100`).
- **Conversor em Tempo Real de CPS (Clicks Per Second)**: Altere o CPS (ex: `10 CPS`, `20 CPS`) e o tempo em milissegundos é recalculado instantaneamente.
- **Presets Rápidos**: Botões de 1, 5, 10, 20, 50 e 100 CPS.
- **Proteção contra travamento**: Limite mínimo seguro configurável para evitar sobrecarga do sistema.

### 3.  Burst Mode (Rajada de Cliques)
- Configure número de cliques por rajada (ex: `5 cliques`), intervalo entre cada clique (`50 ms`) e tempo de espera entre rajadas (`1000 ms`).

### 4.  Randomização Humanizada
- **Intervalo Aleatório**: Define limites mínimo e máximo (ex: `80 ms - 150 ms`) gerando intervalos imprevisíveis para evitar bloqueios anti-cheat.
- **Randomização de Posição (Jitter)**: Adiciona uma variação aleatória de pixels dentro de um raio configurável (ex: `±3 px`).

### 5.  Multi Point Clicker (Multi Click)
- Tabela interativa para sequenciar múltiplos pontos na tela com tempos de espera e botões específicos:
  $$\text{Ponto 1 } (X_1, Y_1) \longrightarrow \text{Ponto 2 } (X_2, Y_2) \longrightarrow \text{Ponto 3 } (X_3, Y_3) \dots$$
- Suporta repetição em loop contínuo ou número fixo de ciclos.

### 6.  Macro Recorder & Player
- Gravador global via `WH_MOUSE_LL` para registrar coordenadas, cliques, descidas/subidas de botão e tempos de espera.
- Reprodução com multiplicador de velocidade (`0.5x`, `1.0x`, `1.5x`, `2.0x`, `3.0x`).
- Exportação e importação de sequências em formato JSON.

### 7.  Safe Zones (Zonas Protegidas)
- Crie zonas retangulares na tela onde cliques são proibidos (ex: botão de fechar aplicativo, barra de tarefas, chat).
- Desenhe as áreas diretamente sobre a tela através do seletor visual integrado.

### 8.  Restrição por Janela Ativa
- Escolha uma janela aberta (ex: `Roblox`, `Discord`, `Chrome`, `Notepad`).
- O Gabriel Autoclick só clica quando a janela alvo estiver focada no primeiro plano, pausando automaticamente ao trocar de janela.

### 9.  Mini Mode (HUD Flutuante)
- Modo compacto e flutuante contendo indicador de status, CPS atual e botão de Iniciar/Parar.
- Pode ser arrastado livremente e mantido sempre sobre outras telas (`Always on Top`).

### 10. ⌨️ Global Hotkeys & Panic Button
- Funciona globalmente mesmo com a janela minimizada ou em segundo plano:
  - **`F6`**: Iniciar / Parar Autoclicker
  - **`F7`**: Abrir Mira para Selecionar Ponto
  - **`F8`**: Iniciar Reprodução de Macro
  - **`F9`**: Parar Macro
  - **`F12` ou `ESC`**:  **PANIC BUTTON** — Interrompe imediatamente qualquer clique, macro, timer ou sequência ativa.

### 11.  Sistema de Perfis
- Perfis prontos inclusos:
  - **Blox Fruits (Farm)**: 12 CPS com Follow Cursor
  - **Minecraft PvP**: Cliques rápidos com randomização de intervalo e jitter
  - **Velocidade Máxima (20 CPS)**
  - **Trabalho (Auto-Refresh)**: 1 clique a cada 5 segundos em ponto fixo
- Crie, duplique, salve, importe e exporte seus próprios perfis em JSON.

### 12.  Painel de Estatísticas
- Contador de cliques na sessão e lifetime acumulado.
- Tempo ativo, CPS médio, maior CPS registrado e macros executadas.
- Persistência automática em `%APPDATA%\GabrielAutoclick\statistics.json`.

---

## 📂 Estrutura do Projeto

```
GabrielAutoclick/
│
├── GabrielAutoclick.sln            # Solução do Visual Studio / .NET CLI
├── build-release.bat               # Script automatizado para compilar o executável final
├── README.md                       # Documentação completa
│
├── GabrielAutoclick/
│   ├── GabrielAutoclick.csproj     # Configuração do projeto WPF x64 .NET 8
│   ├── App.xaml                    # Recursos, paleta de cores e estilos glassmorphism
│   ├── App.xaml.cs                 # Tratamento global de exceções
│   ├── MainWindow.xaml             # Interface principal moderna e cards
│   ├── MainWindow.xaml.cs          # Integração dos serviços e lógica da UI
│   ├── config.json                 # Configuração padrão de fábrica
│   │
│   ├── Models/
│   │   ├── ClickSettings.cs        # Modelo de dados das configurações de clique
│   │   ├── ClickPoint.cs           # Ponto para Multi Point Clicker
│   │   ├── MacroAction.cs          # Ação de gravação/reprodução de macro
│   │   ├── Profile.cs              # Perfil de usuário
│   │   ├── SafeZone.cs             # Definição geométrica da zona protegida
│   │   ├── WindowTarget.cs         # Janela alvo para restrição
│   │   ├── AppStatistics.cs        # Métricas de estatísticas
│   │   ├── HotkeyConfig.cs         # Mapeamento de atalhos globais
│   │   └── AppSettings.cs          # Preferências do sistema
│   │
│   ├── Services/
│   │   ├── AutoClickService.cs     # Motor de autoclick em background thread
│   │   ├── MouseInputService.cs    # Windows API SendInput e temporizador de precisão
│   │   ├── MultiClickService.cs    # Motor de execução multi-pontos
│   │   ├── MacroService.cs         # Hook global WH_MOUSE_LL e reprodução
│   │   ├── HotkeyService.cs        # Hook global de teclado e Panic Button
│   │   ├── SafeZoneService.cs      # Teste de colisão de cursor em Safe Zones
│   │   ├── WindowRestrictionService.cs # Monitoramento de janela ativa
│   │   ├── ProfileService.cs       # Gerenciamento e persistência de perfis
│   │   ├── SettingsService.cs      # Salvamento em %APPDATA%
│   │   ├── StatisticsService.cs    # Registro de métricas em %APPDATA%
│   │   └── SoundService.cs         # Feedbacks sonoros e bips sintéticos
│   │
│   ├── Helpers/
│   │   ├── NativeMethods.cs        # P/Invoke Win32 (SendInput, Hooks, Timer)
│   │   └── Constants.cs            # Constantes e caminhos de arquivos
│   │
│   ├── Views/
│   │   ├── MiniWindow.xaml         # HUD flutuante Mini Mode
│   │   ├── MiniWindow.xaml.cs
│   │   ├── PointPickerOverlay.xaml # Mira transparente em tela cheia
│   │   ├── PointPickerOverlay.xaml.cs
│   │   ├── SafeZonePickerOverlay.xaml # Seleção retangular de Safe Zone
│   │   └── SafeZonePickerOverlay.xaml.cs
│   │
│   └── Assets/
│       ├── background.jpg          # Wallpaper local
│       └── icon.ico                # Ícone em alta resolução
│
└── release/
    └── GabrielAutoclick.exe        # Executável portátil Single-File x64
```

---

## 🛠️ Como Executar em Desenvolvimento

### Pré-requisitos
- **Windows 10 ou Windows 11 (64-bit)**
- **.NET 8.0 SDK** (ou superior)

### Executando via Terminal:
```bash
dotnet restore
dotnet run --project GabrielAutoclick/GabrielAutoclick.csproj
```

---

## 📦 Como Gerar o Executável (.EXE)

Para compilar a versão final independente (Self-Contained Single-File) que funciona em qualquer computador Windows sem necessidade de instalar o .NET ou Visual Studio:

### Opção 1: Executar o script automatizado
Basta dar um duplo clique no arquivo:
```cmd
build-release.bat
```

### Opção 2: Linha de comando manual
```cmd
dotnet publish GabrielAutoclick/GabrielAutoclick.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release
```

O executável final estará disponível em:
```
release\GabrielAutoclick.exe
```

---

## ⌨️ Tabela de Atalhos Padrão

| Atalho | Função | Descrição |
|---|---|---|
| **`F6`** | **Iniciar / Parar** | Alterna o estado do autoclicker principal |
| **`F7`** | **Selecionar Posição** | Abre a mira na tela para capturar coordenada `(X,Y)` |
| **`F8`** | **Iniciar Macro** | Inicia a reprodução da macro gravada |
| **`F9`** | **Parar Macro** | Interrompe a reprodução da macro |
| **`F12` / `ESC`** | ** Panic Button** | Interrompe imediatamente qualquer clique, macro ou timer |

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
   - Execute o `GabrielAutoclick.exe` como Administrador para que a API `SendInput` tenha privilégios de enviar eventos para o jogo elevado.
2. **O autoclicker pausou repentinamente:**
   - Verifique se a opção **Restrição por Janela** ou **Safe Zones** está ativa e se o mouse entrou em uma área protegida.
3. **Pressionei ESC e tudo parou:**
   - O botão `ESC` e a tecla `F12` são mapeados como Panic Button para segurança imediata.

---

**Desenvolvido com excelência para automação ágil e precisa.**
