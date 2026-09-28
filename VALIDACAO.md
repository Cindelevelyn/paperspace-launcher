# Validação do MVP

Data: 28/09/2026. Ambiente: Windows da máquina do usuário, .NET Framework, compilador C# local.

## Build final

Compilação sem erros. Código e recursos XAML incorporados ao executável. Nenhum pacote externo ou ferramenta de desenvolvimento foi instalado.

## Testes automatizados finais

Nove testes passaram:

1. Configuração inicial, gravação JSON e persistência de caracteres Unicode.
2. Rejeição de configuração incompatível sem sobrescrever arquivo válido.
3. Recuperação de arquivo corrompido com preservação do original e backup válido.
4. Preservação do arquivo corrompido quando não há backup.
5. Rejeição de esquemas de URL não permitidos e URLs inválidas.
6. Rejeição de executável inexistente e arquivo com extensão inadequada.
7. Rejeição de referências inexistentes e IDs duplicados.
8. Ida e volta de argumentos com espaços, aspas, barras finais e Unicode, usando o parser nativo do Windows.
9. Preparação de execução sem interpretador de comandos.

O relatório original da execução final está em `validation-results.txt`.

## Abertura real de processos

O décimo teste, com um processo filho auxiliar, passou nas primeiras compilações. Numa repetição posterior, o Controle de Aplicativo do Windows bloqueou o auxiliar recompilado (`LaunchProbe.exe`). Essa repetição não foi considerada aprovada e nenhuma proteção foi desativada. O teste passou a ser opcional (`-ProcessTest`), separado dos nove testes de lógica.

A janela do launcher revisado abriu normalmente na área de trabalho. Durante a experimentação da interface foi observada a abertura do Bloco de Notas por um ambiente Gaming, com registro de conclusão.

## Interface

- Janela e formulário de edição observados pelo controle de computador.
- Problema de contraste dos botões principais identificado e corrigido.
- Oito telas renderizadas pela própria aplicação sem exceções: Início, Dev, Biblioteca e Preferências nos temas claro e escuro.
- Imagens de início e ambiente inspecionadas visualmente.
- Campos e botões expostos na árvore de acessibilidade do Windows.

Os dados de renderização são isolados dos dados do usuário. Isso não equivale a testar todos os clientes de jogos, todos os monitores/DPI ou todas as versões do Windows. Abertura de cada jogo depende de um atalho/alvo válido e do respectivo cliente instalado.
