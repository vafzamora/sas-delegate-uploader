# SAS Upload PoC

Prova de conceito em C# com:

- ASP.NET Core Minimal API que autoriza uploads;
- User Delegation SAS curta, HTTPS-only e restrita a um único blob;
- cliente console que solicita a autorização e envia o arquivo diretamente ao
  Azure Blob Storage.

O backend usa `DefaultAzureCredential`. Localmente ele pode usar a identidade
autenticada no Azure CLI; no Azure, use uma Managed Identity. Nenhuma account
key ou connection string é necessária.

## Arquitetura

```text
Cliente console/aplicação
        |
        | 1. autentica-se na API e solicita autorização
        v
Web API
        |
        | 2. gera User Delegation SAS curta e específica
        v
Cliente console/aplicação
        |
        | 3. faz upload diretamente com HTTPS + SAS
        v
Azure Blob Storage
```

## Pré-requisitos

- .NET 10;
- Azure CLI autenticado com `az login`;
- Storage Account `sasuploadstorage`;
- container privado `uploads`;
- identidade usada pela API com as atribuições RBAC descritas abaixo;
- conectividade entre a identidade/API e o data plane do Storage.

## Configuração

PowerShell:

```powershell
$env:ApiSecurity__ApiKey = "troque-por-um-valor-longo-e-aleatorio"
$env:UploadStorage__AccountEndpoint = "https://sasuploadstorage.blob.core.windows.net/"
$env:UploadStorage__ContainerName = "uploads"
$env:UploadStorage__SasLifetimeMinutes = "5"
$env:UploadStorage__MaxUploadSizeBytes = "524288000"
```

Os nomes de configuração abaixo usam `__` porque mapeiam a hierarquia do
`appsettings.json` para variáveis de ambiente:

```text
ApiSecurity__ApiKey
UploadStorage__AccountEndpoint
UploadStorage__ContainerName
UploadStorage__SasLifetimeMinutes
UploadStorage__MaxUploadSizeBytes
```

Para depuração no VS Code, o `launch.json` já possui essas variáveis.

Não grave API keys reais no repositório. Em produção, substitua a API key
compartilhada desta PoC pela autenticação real do usuário, armazene segredos
em uma solução apropriada como Key Vault e use Managed Identity para acessar
o Storage.

Execute a API:

```powershell
dotnet run --project .\SasUpload.Api
```

Em outro terminal:

```powershell
$env:SAS_UPLOAD_API_KEY = $env:ApiSecurity__ApiKey
$env:SAS_UPLOAD_API_URL = "http://localhost:5017"

dotnet run --project .\SasUpload.Client -- --file .\arquivo.txt
```

O cliente mostra somente o nome do blob e a expiração. Ele não imprime a URL
completa com o SAS.

## Roles obrigatórias para User Delegation SAS

A identidade efetiva usada pela API precisa de duas capacidades distintas.

### 1. `Storage Blob Delegator`

Permite solicitar uma User Delegation Key à Storage Account. Deve ser atribuída
no escopo da conta:

```powershell
$identityObjectId = "<object-id-da-identidade-efetiva>"
$accountId = az storage account show `
  --name sasuploadstorage `
  --resource-group "<resource-group>" `
  --query id `
  --output tsv

az role assignment create `
  --assignee-object-id $identityObjectId `
  --assignee-principal-type User `
  --role "Storage Blob Delegator" `
  --scope $accountId
```

Para uma Managed Identity ou Service Principal em produção:

```powershell
--assignee-principal-type ServicePrincipal
```

### 2. `Storage Blob Data Contributor`

Concede acesso ao data plane e é necessário porque a autorização final do
User Delegation SAS é a interseção entre:

- permissões solicitadas no SAS; e
- permissões atuais da identidade que gerou a User Delegation Key.

Prefira atribuir esta role apenas ao container `uploads`:

```powershell
$containerScope = "$accountId/blobServices/default/containers/uploads"

az role assignment create `
  --assignee-object-id $identityObjectId `
  --assignee-principal-type User `
  --role "Storage Blob Data Contributor" `
  --scope $containerScope
```

Use `ServicePrincipal` no parâmetro acima quando a identidade for uma Managed
Identity. Se a solução precisar apenas emitir SAS de escrita, uma role
customizada com as ações data plane mínimas pode substituir
`Storage Blob Data Contributor`, mas a role padrão simplifica a PoC.

Confira as atribuições, incluindo herança:

```powershell
az role assignment list `
  --assignee $identityObjectId `
  --scope $accountId `
  --include-inherited `
  --output table
```

Novas atribuições podem levar alguns minutos para propagar.

## Identidade efetivamente utilizada

`DefaultAzureCredential` representa uma cadeia de credenciais. Possuir uma role
no portal não é suficiente se a aplicação estiver usando outra identidade ou
outro tenant.

Durante o desenvolvimento local, confirme a identidade:

```powershell
az account show --query "{user:user.name, tenant:tenantId, subscription:id}"
az ad signed-in-user show --query "{objectId:id, userPrincipalName:userPrincipalName}"
```

Também é possível solicitar um token do scope do Storage e inspecionar suas
claims no debugger:

```csharp
using Azure.Core;

var credential = new DefaultAzureCredential(
    new DefaultAzureCredentialOptions
    {
        ExcludeVisualStudioCredential = true
    });

var token = credential.GetToken(
    new TokenRequestContext(
        ["https://storage.azure.com/.default"]));
```

No JWT, `oid` identifica o principal efetivo, `tid` identifica o tenant e
`appid` ou `azp` identifica a aplicação. `Storage Blob Delegator` e
`Storage Blob Data Contributor` devem ser atribuídas ao mesmo `oid` e no mesmo
tenant que gerou o token.

Não imprima `token.Token`, SAS URLs ou tokens completos em logs, console ou
telemetria. Inspecione apenas claims necessárias no debugger. Em produção,
remova o diagnóstico local de token.

## Rede e acesso privado

O container deve permanecer privado:

```text
publicAccess = None
```

Isso bloqueia acesso anônimo, mas não é incompatível com OAuth, RBAC ou SAS.

Não confunda acesso anônimo do container com `publicNetworkAccess` da Storage
Account:

- `publicAccess: None`: o blob exige autenticação/SAS;
- `publicNetworkAccess: Disabled`: o endpoint da conta não aceita chamadas da
  internet, mesmo que sejam autenticadas;
- firewall de Storage: pode negar IPs, mesmo quando o principal possui RBAC;
- Private Endpoint: exige DNS privado e uma rota/VPN entre o processo e a conta.

Se a policy corporativa desabilitou o acesso público, executar apenas a Web API
no computador local não garante conectividade ao Storage. Opções:

1. usar Private Endpoint e VPN/rede conectada;
2. executar a API em uma rede com acesso ao endpoint privado;
3. solicitar uma exceção aprovada e temporalmente limitada à policy;
4. não utilizar como produção: liberar o endpoint público somente para a PoC e
   restringir o firewall ao IP necessário.

Não contorne policies corporativas.

## Fluxo da autorização e do upload

1. O cliente envia nome, MIME type e tamanho do arquivo para a API.
2. A API autentica a chamada com `X-Api-Key` nesta PoC.
3. A API valida tamanho, extensão e MIME type.
4. O backend cria um nome de blob exclusivo em `uploads/`.
5. A identidade do backend obtém uma User Delegation Key.
6. A API retorna uma SAS HTTPS de curta duração, somente com `create` e `write`,
   para aquele blob.
7. O cliente envia o arquivo diretamente ao Storage.
8. `If-None-Match: *` impede sobrescrita do blob.

Permissão `Read` não é incluída no SAS. A URL também não permite listar,
deletar blobs ou acessar outros arquivos.

## Diagnóstico de HTTP 403

Durante a geração da User Delegation Key, um `403` indica normalmente:

- falta de `Storage Blob Delegator`;
- Storage Account inacessível pela rede atual;
- token emitido para outro tenant ou identidade;
- RBAC ainda não propagado.

Durante o upload realizado com a URL SAS:

- `AuthorizationPermissionMismatch`: a identidade que criou a delegação
  provavelmente não possui `Storage Blob Data Contributor`, ou o SAS não
  contém as permissões necessárias;
- `AuthorizationFailure` ou `AuthorizationSourceIPMismatch`: firewall,
  `publicNetworkAccess` ou vínculo de IP;
- `AuthenticationFailed`: token SAS expirado, assinatura inválida ou relógio
  inconsistente.

Confirme o `ErrorCode`, status, request ID e headers da exceção
`RequestFailedException`; não registre o query string do SAS.

Também verifique:

```powershell
az storage account show `
  --name sasuploadstorage `
  --resource-group "<resource-group>" `
  --query "{publicNetworkAccess:publicNetworkAccess, allowBlobPublicAccess:allowBlobPublicAccess, defaultAction:networkRuleSet.defaultAction}" `
  --output json
```

## Limitações da PoC

- A API key identifica um cliente compartilhado, não um dispositivo real.
- Um SAS é uma credencial bearer: quem possuir a URL pode usá-la até expirar.
- A API key não deve ser compilada no aplicativo móvel.
- Em produção, exija a sessão do usuário e assine as solicitações com uma
  chave privada registrada para cada dispositivo.
- Considere App Attest no iOS e Play Integrity/Key Attestation no Android
  durante o registro do dispositivo.
- Use rate limiting por usuário, dispositivo e IP.
- Use container de quarentena e lifecycle policies para uploads abandonados.
- Valide assinatura, malware e conteúdo real posteriormente; extensão e MIME
  type enviados pelo cliente não constituem prova do conteúdo.
- Não registre URL SAS em logs, analytics ou ferramentas de observabilidade.
- Para impedir uso fora do aplicativo de forma estrita, considere upload pelo
  backend, mTLS ou outro mecanismo de prova criptográfica por operação.

## Segurança de produção

Recomendações:

- HTTPS obrigatório;
- SAS com expiração curta, como 5 minutos;
- um nome de blob gerado pelo backend;
- Managed Identity, sem account keys;
- menor escopo RBAC possível;
- container privado e Storage sem acesso anônimo;
- prevenção de sobrescrita;
- expiração/limpeza de blobs temporários;
- rotação/revogação da identidade;
- auditoria sem registrar credenciais.

## Referências

- [Autenticação do Azure Storage](https://learn.microsoft.com/azure/storage/common/storage-auth)
- [Criar User Delegation SAS](https://learn.microsoft.com/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet)
- [Roles de acesso do Blob Storage](https://learn.microsoft.com/azure/storage/blobs/assign-azure-role-data-access)
- [Configurar firewalls e redes virtuais do Storage](https://learn.microsoft.com/azure/storage/common/storage-network-security)
