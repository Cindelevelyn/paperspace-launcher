# PaperSpaces — MVP 0.1.0

Um launcher local de ambientes para Windows, com visual de caderno, marcadores laterais e temas claro/escuro.

## Abrir

Abra **PaperSpaces.exe** nesta pasta. Não é necessário instalar Node, Rust ou Visual Studio para usar o aplicativo nesta máquina. Mantenha `PaperSpaces.exe.config` ao lado do executável.

Se uma janela de prévia já estiver aberta, feche-a e abra o executável final para carregar a versão atualizada.

O aplicativo usa o .NET Framework 4.8 do Windows. O executável foi compilado localmente e não possui assinatura de distribuição comercial. Não altere as proteções do Windows caso alguma política da sua máquina restrinja sua execução.

## Primeiro ambiente

1. Clique em **Gaming**, **Dev** ou **Study**, à esquerda.
2. Clique em **Adicionar app**. Se o app ainda não estiver na biblioteca, escolha **Cadastrar novo app ou link**.
3. Informe o nome e selecione um `.exe`, um atalho `.lnk` ou um link `https://`.
4. Salve. Use as setas para definir a ordem de abertura.
5. Clique em **Ativar ambiente**. O launcher aplica seu próprio tema e solicita a abertura dos apps em sequência.

Para jogos, prefira o atalho que o Steam, Riot Client ou Battle.net criou. Alguns executáveis internos do jogo não funcionam sem o cliente responsável. O cadastro manual permite incluir qualquer cliente sem depender de detecção automática.

Os exemplos iniciais são **Bloco de Notas** e **GitHub**. Gaming começa vazio; Dev usa os dois exemplos; Study usa Bloco de Notas. Nada abre apenas por selecionar uma aba.

## Funcionalidades entregues

- Criar, editar e excluir ambientes.
- Personalizar nome, descrição, cor, símbolo, tema e minimização após ativação.
- Biblioteca compartilhada entre ambientes, com busca por nome.
- Cadastrar, editar e excluir executáveis, atalhos e links HTTP/HTTPS.
- Argumentos opcionais para `.exe`, um argumento por linha, sem envolver cada linha em aspas adicionais.
- Adicionar/remover apps de um ambiente e alterar sua ordem.
- Abrir um app individual ou toda a sequência.
- Informar falhas de abertura e continuar os próximos itens.
- Impedir ativações simultâneas no mesmo processo e múltiplas instâncias sobre a mesma pasta de dados.
- Temas claro/escuro; formulário acessível por teclado; Escape fecha formulários.
- Configuração local, gravação por substituição de arquivo e backup automático.
- Recuperação de configuração corrompida quando há backup válido.
- Exportar configuração em Preferências.

## Onde ficam os dados

```text
PaperSpaces/
  PaperSpaces.exe
  PaperSpaces.exe.config
  LEIA-ME.md
  VALIDACAO.md
  source/                 código-fonte e script de compilação
  previews/               imagens dos dois temas
  data/
    config.json           configuração atual
    config.json.bak       versão anterior
```

A pasta `data` é criada na primeira abertura. Para transferir o launcher mantendo seus ambientes, copie a pasta toda. Para fazer backup, use **Preferências → Exportar configuração** ou copie `data` com o app fechado. Para restaurar manualmente, feche o launcher e substitua `data/config.json` por um backup válido, preservando antes o arquivo atual.

O histórico de abertura aparece somente durante a sessão. Ele não é enviado a nenhum serviço. A abertura de um link usa seu navegador padrão.

As configurações de prévias eventualmente usadas durante a criação também podem estar preservadas em `backups-de-previas`. Elas não são mescladas automaticamente com seus dados atuais.

## Limites deste MVP

- O tema é do launcher; ele não altera o tema do Windows.
- Não inclui wallpaper, volume, restauração de configurações do sistema, Alexa, detecção automática de jogos, bandeja, inicialização automática ou widgets de hardware.
- Fechar encerra o launcher; minimizar mantém a janela na barra de tarefas.
- Apps já abertos podem receber outra solicitação de abertura; cada aplicativo decide se reaproveita sua janela.
- “Abertura solicitada” confirma o envio da solicitação, não que o jogo terminou de carregar ou autenticar.
- Não fecha programas abertos pelo ambiente.
- Atalhos `.lnk` usam os argumentos configurados nas propriedades do próprio atalho.
- Limites de dados: até 100 ambientes, 500 apps na biblioteca e 100 apps por ambiente.

## Tecnologia

Este MVP foi implementado em **C# + WPF**, usando o compilador .NET já presente na máquina. A proposta anterior de Tauri/React exigiria instalar Rust e as ferramentas C++ que não estavam disponíveis. Não foi criado um projeto Tauri nem um instalador; esta entrega é um aplicativo portátil com código-fonte completo.

## Recompilar

Feche o executável antes de recompilar por cima dele. Em um PowerShell aberto na pasta PaperSpaces:

```powershell
.\source\build.ps1
```

Compilar e rodar os testes de dados e validação:

```powershell
.\source\build.ps1 -Test
```

Teste opcional com processo filho real:

```powershell
.\source\build.ps1 -ProcessTest
```

O teste opcional gera `LaunchProbe.exe`, que só grava os argumentos recebidos num arquivo de teste. Políticas de controle de aplicativos do Windows podem impedir esse auxiliar. Veja `VALIDACAO.md`.

Compilar em outra pasta:

```powershell
.\source\build.ps1 -OutputDirectory C:\caminho\para\build
```

## Mapa do código

| Arquivo | Responsabilidade |
| --- | --- |
| `source/App.cs` | Janela, navegação, formulários e ativação de ambientes |
| `source/Main.xaml` | Estrutura da janela e marcadores laterais |
| `source/Theme.xaml` | Recursos de cores e estilos de controles |
| `source/Models.cs` | Modelos, validação, persistência e execução de apps |
| `source/Tests.cs` | Testes de persistência, recuperação, validação e argumentos |
| `source/LaunchProbe.cs` | Auxiliar isolado do teste de processo filho |
| `source/build.ps1` | Compilação com o .NET Framework disponível no Windows |

## Próximas entregas sugeridas

1. Importação pela interface e detecção de atalhos instalados.
2. Wallpaper por ambiente com snapshot e restauração.
3. Bandeja e atalho global.
4. Provedores de jogos, começando por Steam.
5. Integração com Alexa usando autenticação e comandos por ID.
