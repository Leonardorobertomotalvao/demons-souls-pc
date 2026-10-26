# 35. Otimização da preparação do jogo no launcher (antes/depois medidos)

Data: 2026-10-26 · Launcher `0.2.1-alpha.1` · branch `feat/iso-fullflow` · máquina de referência: Windows 10, Ryzen 5 5500 (6c/12), 16 GB RAM, HDD ~46–120 MB/s.

## Objetivo

O PREPARAR (importar a instalação gerenciada a partir da ISO) re-hasheava e relia
demais: **413 s** (CLI) e **422,9 s** (GUI) para preparar em cima de uma instalação
já íntegra de 8,77 GiB (12.512 arquivos), sendo ~191 s na origem + ~212 s no destino
(traces 5ef39121, 2026-10-26 09:2x; `import-repair.log`, 09:48:22→09:55:15). Um
segundo problema: `--verify` (CLI) e VERIFICAR (GUI) **falhavam** em instalações de
uso real porque validadores tratavam artefatos gerados pelo runtime como divergência.

## O que mudou (resumo técnico)

1. **Correção dos validadores**: `.arealoadlist` gerados pelo patch de 60 FPS
   (`vfs/PS3_GAME/USRDIR/(map|ship_ocean)/*.arealoadlist`) e buckets vazios de cache
   runtime (`00`/`01`) passaram a ser aceitos como artefatos legítimos. A rejeição
   honesta de conteúdo divergente de mesmo tamanho não mudou (testes embutidos).
2. **Destino confirmado por identidade NTFS/USN** (`EntryCanReuse`): com manifesto
   Complete + MAC DPAPI válido, cada entrada é confirmada pela própria identidade
   NTFS (volume, file id, tamanho, datas, USN do journal). Sem divergência: **zero
   leituras do destino**. Divergência real: fallback de hash só do arquivo divergente.
3. **Identidade da imagem de origem** (`installation-source.json`, assinada por HMAC
   com a chave DPAPI local): registra identidade NTFS/USN da ISO e a origem usada.
   Re-preparação com a mesma imagem: **zero leituras da origem** (hashes já
   registrados reutilizados). Imagem diferente, registro ausente/adulterado: re-hash
   honesto completo da origem (testes: registro, reuso e tamper-fallback).
4. **Hashing paralelo limitado** (`RunBounded`, grau `min(núcleos,8)`,
   `DEMONS_SOULS_PREP_CONCURRENCY` 1–16): primeiro-erro interrompe o agendamento e
   é re-lançado ao final; cancelamento propaga igual.
5. **Cópia com SHA-256 em voo**: arquivo a restaurar é copiado e hasheado numa
   única passada (sem leitura dupla da origem).
6. **UI**: vazão média em tempo real e resumo final honesto (nada recopiado × X
   bytes recopiados/corrigidos); sondas `PROBE eboot_click` removidas.

Instrumento novo: `--import --source-image <ISO>` dá à CLI a mesma identidade de
origem que a GUI já usa.

## Metodologia

CLI com `Start-Process -Wait -RedirectStandardOutput/Error` (o redirecionamento
`*>` do PS 5.1 falha silenciosamente — proteção documentada em invocação anterior),
ISO montada read-only numa letra de volume própria (a origem montada vive no MESMO
disco físico da instalação), destino = a instalação de teste local íntegra de
8,77 GiB (12.512 arquivos). Números "antes": import-repair.ps1 (método
Start-Process, 413 s wall) e traces GUI do usuário (422,9 s). Cada linha JSON de
progresso (1 Hz) e os counters do manifesto
participam do relato; **a métrica primária é bytes lidos via API**, porque o wall
de leituras repetidas em minutos curtos é influenciado pelo cache de página do SO
(16 GB RAM ≥ 8,77 GiB de dados) — dizer "re-leu 8,366 MiB em segundos" sem essa
nota seria vaidade, não medição.

## Resultados "depois" (2026-10-26, 10:21–10:26)

### Cadeia sem identidade de imagem (CLI `--import` puro — na prática: origem sempre re-hasheada)

| Cenário | Wall | Fases (contadores honestos) |
|---|---|---|
| A1 importar sobre íntegra (1ª run neste build) | **209,4 s** | origem: re-hash **8.366 MiB** lidos; destino: **0 bytes lidos** (USN — manifesto do binário anterior confirmado!); cópia: 0 bytes (12.512 reaproveitados) |
| A1b importar de novo (mesmo minuto) | 12,0 s | origem re-hasheada **8.366 MiB via API**, mas dados no cache de página (nota de honestidade acima); destino 0; cópia 0 |
| A2 `--verify` | **4,0 s** | instalação confirmada por identidade NTFS/USN, **0 bytes lidos** |
| A3 reparo (vítima `ds_goodending.pam`, 450 MiB, deletada) | **18,0 s** | origem re-hash (morna; frio ≈ 191 s sem identidade); destino 0 (USN); **450 MiB copiados com SHA em voo**, restaurada byte-exata |
| A4 `--fps-compatibility` | 2,0 s | aprovado |

Comparativo direto: **413 → 209 s (2,0×)** na primeira campanha sobre íntegra
(a origem ainda é o custo); **413 → 18 s (23×)** no reparo de arquivo (disco morno).

### Cadeia com identidade de imagem (`--source-image`, paridade GUI)

| Cenário | Wall | Contadores |
|---|---|---|
| S1 primeira campanha com imagem (registra `installation-source.json`) | **12,1 s** | origem re-hasheada **8.366 MiB via API** (disco morno — primeira leitura de origem sob identidade); destino **0 bytes** (USN); cópia **0 bytes**; side-file gravado e assinado |
| S2 re-campanha (identidade reaproveitada) | **9,1 s** | origem confirmada pela identidade da imagem: **0 bytes lidos**; destino **0 bytes**; cópia **0 bytes** — preparação completa do jogo com **zero leituras** sobre 8,77 GiB |
| S3 reparo com imagem (vítima restaurada) | **16,1 s** | origem **0 bytes** (identidade); destino **0 bytes** (USN); **472.053.760 bytes recopiados com SHA-256 em voo**, vítima restaurada byte-exata |

## Limites e honestidade

- Todos os ganhos acima medidos em **uma** máquina e HDD; SSD ou outro hardware
  podem mudar os absolutos (os mecanismos contam bytes lidos, não a sorte do cache).
- A origem continua o gargalo da primeira campanha sem identidade (8,37 GiB de
  hash): a otimização real da primeira campanha é o hashing limitado em paralelo;
  os ganhos grandes vêm nas campanhas seguintes (identidade de imagem) e no
  reparo.
- JOGAR (runtime) já usava USN há versões: 11,9–77 s classe histórica — sem
  alteração nesta missão.
- **`--import` de CLI não grava trace JSONL** (decisões como
  `source_hash_confirmed_by_image_identity` só existem quando um trace está ativo,
  como nas operações de GUI): o relato por fase desta crônica vem das linhas JSON
  de progresso do stdout (1 Hz) — nada foi reconstituído a partir de logs que não
  existem. Medir/benchmarkable a identidade por CLI foi possível com o novo
  `--source-image`.
- **Citação de argumentos no PowerShell 5.1**: `Start-Process -ArgumentList` não
  cita elementos com espaços — caminhos devem ser passados como `'"'+$path+'"'`.
  O trunca-andamento histórico foi reproduzido e capturado.
- Traces completos (JSONL com fases, bytes e decisões) ficam em
  `%LOCALAPPDATA%\DemonsSoulsPCLauncher\logs\operations\*.jsonl` da máquina de
  referência; **não** são publicados (conteúdo operacional do usuário).
