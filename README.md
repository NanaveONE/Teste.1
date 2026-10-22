# Xbox ISO Extractor Mobile — Android MVP

**Código do motor integrado de verdade** a partir do repositório `dromex1/XboxImageExtractor` (MIT): `GdfxArchive`, `GdfxEntry`, `GdfxExtractor`. A interface WinForms foi substituída por .NET MAUI (C#) para Android.

## Funcionalidades no código

- Abrir ISO com seletor nativo do Android (Storage Access Framework / SAF).
- Detectar cabeçalho GDFX/XISO e ler árvore de diretórios, incluindo layout de Xbox clássico/XGD2/XGD3 reconhecido pelo parser de origem.
- Pré-visualizar até 150 itens (com contagem total de arquivos e diretórios).
- Selecionar uma pasta de saída usando o seletor nativo do Android.
- Extrair conteúdo com progresso e cancelamento; sem copiar a ISO inteira para RAM ou cache.
- Sem permissão de acesso irrestrito ao armazenamento.

**Ainda não foi compilado ou executado num Android real.** Compatibilidade não validada com ISO reais. Alguns provedores `content://` não suportam leitura por offset; nesse caso é necessário copiar a ISO para armazenamento local e selecioná-la de lá. Os arquivos já criados podem permanecer após falha ou cancelamento. Selecionar uma subpasta nova e vazia evita mistura com extrações anteriores.

## Compilação do APK

Pré-requisitos: Windows/macOS/Linux com **.NET 8 SDK**, workload Android de MAUI, Android SDK (API apropriada) e JDK compatível. O SDK MAUI é mais simples de configurar via Visual Studio 2022 com workload .NET MAUI, especialmente no Windows.

Na pasta que contém `XboxImageExtractorMobile.csproj`:

```bash
dotnet workload install maui-android
dotnet restore
dotnet build XboxImageExtractorMobile.csproj -f net8.0-android -c Debug
```

O APK de desenvolvimento ficará normalmente em `bin/Debug/net8.0-android/` (verifique o nome gerado pelo SDK). Para solicitar explicitamente um APK:

```bash
dotnet publish XboxImageExtractorMobile.csproj -f net8.0-android -c Debug -p:AndroidPackageFormat=apk
```

O arquivo resultante é **APK de teste/debug**; distribuição pública exige assinatura e configuração de Release. **Termux não inclui, por padrão, o toolchain Android MAUI**, e este ambiente de elaboração não dispõe de `dotnet` nem Android SDK para compilar o APK.

## Estrutura

- `Core/GdfxArchive.cs` — parser GDFX a partir de `Stream` com leitura aleatória; checagens de offsets.
- `Core/GdfxEntry.cs` — nó de arquivo/diretório.
- `Core/GdfxExtractor.cs` — extrator via interface `IExtractionFolder`, progresso, cancelamento, validação de caminhos.
- `Services/AndroidIsoStream.cs` — leitura posicional via descritor SAF e `pread` Android.
- `Services/AndroidSafFolder.cs` — criação de diretórios e arquivos via `DocumentsContract`.
- `Services/AndroidDocumentPicker.cs` — seleção de ISO/pasta via Android SAF.
- `Platforms/Android/` — MainActivity, MainApplication, Manifest.
- `MainPage.cs` — tela MAUI.

## Limitações e validação necessária

1. Verificar compilação Android de bindings (`Android.Systems.Os.Pread`, `DocumentsContract`) em SDK instalado.
2. Testar leitura/extração de ISO XISO, XGD2 e XGD3 próprias com conteúdos verificáveis.
3. Testar seleção de pasta SAF em vários Androids, inclusive restrições do Android 11+ na raiz de `Downloads` (prefira subpasta).
4. Aplicativo é extrator **de ISO sem criptografia GDFX/XISO**; não remove DRM e não extrai pacotes Xbox modernos.
5. Pastas com permissões ou nomes inválidos podem gerar erros, que aparecem na interface.

## Créditos

Adaptado de https://github.com/dromex1/XboxImageExtractor — licença MIT conforme o README do repositório original. Este projeto não tem vínculo com Microsoft.

## Gerar APK na nuvem pelo GitHub Actions (sem PC)

O projeto inclui `.github/workflows/build-apk.yml`. Crie um **repositório novo** no seu GitHub e faça upload do **conteúdo descompactado** deste ZIP para a raiz desse repositório (inclua a pasta `.github`). Abra **Actions → Compilar APK Android (debug) → Run workflow**. Quando terminar com sucesso, faça download do artifact `XboxImageExtractorMobile-debug-apk` e extraia o `.apk` para instalar no Android.

**Atenção:** este workflow é um ponto de partida e também **não foi executado** aqui; a primeira execução poderá apontar problemas de compatibilidade/build que exigem ajustes. Não sobrescreva o repositório original do autor com este projeto.
