# Changelog — Demon's Souls PC (porte não oficial)

Todas as mudanças notáveis do launcher e do porte neste repositório. Datas em
ISO-8601. Este projeto não publica código, dados ou executáveis do jogo; veja o
README (§9) para compilar a partir da fonte.

## [Launcher 0.2.1-alpha.1] — 2026-10-26 — branch `feat/iso-fullflow`

### Corrigido
- **VERIFICAR travava preparações completas por artefatos gerados pelo runtime**: listas
  de carregamento de área (`.arealoadlist` sob `USRDIR/map` e `USRDIR/ship_ocean`)
  produzidas pelo patch de 60 FPS e buckets vazios de cache runtime (`00`/`01`) eram
  tratados como divergência de conteúdo. A validação estrutural agora os aceita como
  artefatos legítimos do runtime; a detecção de divergência real continua intacta
  (mutação de mesmo tamanho é rejeitada e reparada, com testes embutidos cobrindo os dois
  lados).
- **`--verify` de linha de comando falhava em instalação de uso real** (mesma causa).

### Mudado — desempenho da preparação do jogo (mesma instalação de ~8,77 GiB, HDD)
Números medidos na máquina de referência do projeto (Ryzen 5 5500, 16 GB RAM, HDD
~46–120 MB/s; metodologia e traces em `docs/35-launcher-prepare-optimization.md`):

| Cenário | Antes | Depois | Leitura via API (depois) |
|---|---|---|---|
| Importar sobre instalação íntegra (CLI, origem re-hasheada) | 413 s (wall, todo o fluxo) | **209 s** | destino: **0 bytes** (confirmado por USN); origem: 8,366 MiB re-hasheados |
| Repreparar com a mesma imagem, identidade já registrada (GUI ou CLI `--source-image`) | 413 s (wall, todo o fluxo) | **9,1 s** | **0 bytes lidos** — origem, destino e cópia confirmados por identidade/USN |
| Reparo de 1 arquivo vítima (450 MiB recopiados) | 413 s (wall, todo o fluxo) | **16,1 s** com identidade de imagem (**) | origem/destino: 0 bytes; só o vítima é recopiado com SHA em voo |
| Verificação completa da instalação (`--verify`) | falhava (bug dos validadores) | **4,0 s** | **0 bytes** |
| `--fps-compatibility` | medição indisponível (falha silenciosa da captura no bench "antes") | 2,0 s, aprovado | 0 bytes |

\*\* Os cenários A (sem imagem) e o reparo S3 entenderam a origem "morna" (cache de
página do SO depois dos minutos anteriores): o tempo de parede ganha velocidade
aparente, mas os bytes lidos via API são contados de verdade (S3 leu zero da
origem; A3 re-hasheou os 8,366 MiB). A primeira campanha de verdade sobre disco
frio custa ~191–212 s de origem/destino quando não há identidade registrada — é
exatamente o caso que o registro `installation-source.json` elimina nas
campanhas seguintes (linha 2 da tabela).

- **Destino confirmado por identidade NTFS/USN**: com manifesto completo e confiável
  (`installation.json` + MAC DPAPI + snapshots USN), preparar/verificar sobre uma
  instalação íntegra **não relê a cópia**; qualquer divergência real cai no fallback de
  hash apenas do arquivo divergente. Confirmado inclusive contra manifestos gravados pelo
  binário anterior (A1 leu 0 bytes do destino).
- **Identidade da imagem de origem (`installation-source.json`)**: quando a preparação
  parte da ISO (GUI, ou CLI com o novo `--source-image`), um registro assinado com
  HMAC + identidade NTFS/USN da imagem é gravado no destino; na re-preparação com a
  mesma imagem a origem **não é relida** — os hashes já registrados são reutilizados.
  Divergência da imagem, registro faltante ou violado volta ao re-hash honesto da
  origem (testes de regressão embutidos cobrem registro, reutilização e adulteração).
- **Hashing paralelo limitado** (`RunBounded`, grau padrão
  min(núcleos,8), configurável por `DEMONS_SOULS_PREP_CONCURRENCY`): contratos de
  primeiro-erro e cancelamento preservados.
- **Cópia com SHA-256 em voo**: o arquivo vítima foi recopiado e verificado em uma
  única passada (A3/S3), sem pré-leitura separada da origem.
- **UI (PREPARAR)**: linha de vazão média (MiB/s de leitura via API) durante o
  progresso e **resumo final honesto** ao concluir (quantos arquivos/bytes foram
  confirmados íntegros sem releitura e quantos bytes foram recopiados/corrigidos).
- Foram removidas sondas de diagnóstico temporárias (`PROBE eboot_click`) do log.

### Adicionado
- **CLI `--import --source-image <arquivo.iso>`**: mesma identidade de origem que a GUI
  usa, permitindo reproduzir/medir a reutilização fora da janela WPF (nada muda no fluxo
  da GUI).
- **`CHANGELOG.md`** (este arquivo) e **`docs/35-launcher-prepare-optimization.md`**
  (crônica completa antes/depois com traces e limitações).

### Testes executados nesta versão
- `Release / win-x64`: 0 avisos, 0 erros.
- `--self-test` completo: `SELF_TEST_PASS` (suíte inteira, incluindo os novos grupos
  "bounded parallel hashing", "source-image identity reuse and tamper fallback" e
  "managed Verify USN confirmation keeps content authority").
- Benchmarks A1/A1b/A2/A3 + showcase S1/S2/S3 (identidade de imagem, paridade GUI)
  na instalação real de 8,77 GiB (vítima restaurada byte-exata em todos os cenários
  de reparo; manifesto sempre `Complete`).

### Instalação e atualização
- Compilar a fonte: `dotnet publish launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj -c Release -r win-x64 --self-contained true -o <pasta>`
  (README §9.1) ou o pacote reproduzível `scripts\make_launcher_release.ps1` (README §9.3).
- Atualizar de `v0.2.1-alpha` (22-10-2026): substitua o executável na pasta `Launcher\` do pacote; a
  instalação gerenciada existente (`installation.json`) continua válida — A confirmou isso na prática
  (A1 deste cycle leu **0 bytes do destino** de um manifesto assinado pelo binário v0.2.1-alpha e
  confirmou os 12.512 arquivos por USN). O reuso da origem (linha "identidade de imagem") passa a
  valer a partir da primeira preparação feita com esta versão.
- **CLI `--import --source-image`**: caminhos com espaços (ex. a ISO) devem ser citados quando a
  chamada vai por `Start-Process` no Windows PowerShell 5.1 (`('"'+$iso+'"')`); o CLI agora
  **falha alto** se o caminho informado não existir, em vez de degradar silenciosamente a
  identidade da origem. A GUI não depende dessa citação (WPF passa os caminhos nativamente).
- Requisitos de execução: Windows 10/11 x64, GPU D3D12 FL 12.0+; `.NET` não é necessário
  (self-contained); exatamente as mesmas gates de identidade de runtime/EBOOT das versões anteriores.

### Não testado / pendente (honesto)
- Ganho de velocidade em máquinas/HDs/SSDs diferentes da de referência.
- Preparação através da GUI medida com traces novos de GUI (os números acima são de CLI;
  os traces GUI "antes" (422,9 s: ~191 s origem + ~212 s destino) continuam o contraponto).
- Resto do jogo além da área já testada (limitação do porte, sem relação com o launcher).
