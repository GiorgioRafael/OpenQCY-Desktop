# OpenQCY Desktop

OpenQCY Desktop é um controlador open source para fones QCY no Windows. O primeiro modelo suportado será o **QCY MeloBuds N70 (HT18)**.

> [!IMPORTANT]
> Este é um projeto comunitário independente, sem afiliação, endosso ou suporte da QCY ou da Dongguan Hele Electronics Co., Ltd. QCY e MeloBuds são marcas de seus respectivos proprietários.

## Estado do projeto

O projeto está no primeiro marco da interface nativa. A janela de configurações, o painel da área de notificação, a persistência do perfil local e a descoberta Bluetooth somente leitura já funcionam. Os comandos reais continuam desativados até serem capturados do aplicativo móvel oficial e reproduzidos com segurança.

![Configurações do OpenQCY Desktop](docs/screenshots/main.png)

![Painel do OpenQCY Desktop na área de notificação](docs/screenshots/tray.png)

As porcentagens de bateria e as respostas do fone mostradas neste marco são dados simulados realistas. As preferências locais são reais e persistem entre reinicializações.

Controles cotidianos planejados:

- Bateria dos lados esquerdo e direito e do estojo
- ANC, transparência, modo normal, submodos e intensidade
- Presets e curva personalizada de múltiplas bandas
- Personalização dos gestos de toque
- Detecção de uso com reaplicação automática ao reconectar
- LDAC, multiponto, modo jogo, modo sono e redução de vento
- Volume dos avisos, desligamento após desconexão e localizar fones

Atualização de firmware, conta, telemetria, anúncios e loja estão explicitamente fora do escopo.

## Tecnologia

- C# 14 e .NET 10 LTS
- WinUI 3 / Windows App SDK
- APIs Bluetooth GATT do Windows
- MVVM com CommunityToolkit.Mvvm
- Distribuição portátil e autocontida para Windows 11

A identidade visual se chama **OpenQCY Glass**: uma interpretação nativa do Windows da clareza, profundidade, cantos amplos e expansão contextual das interfaces modernas da Apple. Ela usa controles WinUI nativos e recursos originais do projeto; não distribui fontes, ícones ou imagens da Apple.

## Compilação

Pré-requisitos:

- Windows 11
- SDK do .NET 10

```powershell
dotnet restore -r win-x64
dotnet build -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

O GitHub Actions compila e publica um artefato `win-x64` autocontido. Consulte a [arquitetura](docs/architecture.md), a [pesquisa do protocolo](docs/protocol-research.md) e a [linha de base de desempenho](docs/performance.md).

## Segurança

O OpenQCY Desktop nunca tentará adivinhar comandos no fone. Os bytes do protocolo precisam ser observados em uma captura Bluetooth feita pelo proprietário do dispositivo, documentados, reproduzidos de forma isolada e cobertos por testes antes de serem disponibilizados na interface.

## Licença

[MIT](LICENSE)
