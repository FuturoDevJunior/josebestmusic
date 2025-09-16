# Guia de Publicação no NuGet.org

Este guia explica como publicar os pacotes NetThrottler no NuGet.org.

## 📦 Pacotes Disponíveis

- **NetThrottler.Core** (18KB) - Biblioteca core com interfaces e implementações básicas
- **NetThrottler.AspNetCore** (14KB) - Middleware para ASP.NET Core
- **NetThrottler.Redis** (12KB) - Storage distribuído com Redis

## 🚀 Passos para Publicação

### 1. Criar Conta no NuGet.org

1. Acesse [nuget.org](https://www.nuget.org/)
2. Clique em "Sign in" e crie uma conta Microsoft
3. Complete o processo de verificação

### 2. Gerar API Key

1. Faça login no NuGet.org
2. Vá para **Profile** → **API Keys**
3. Clique em **Create**
4. Preencha:
   - **Key name**: `NetThrottler-Publish`
   - **Package owner**: Seu username
   - **Glob pattern**: `NetThrottler.*`
   - **Expires**: 1 ano
5. Clique em **Create**
6. **Copie a API Key** (você só verá uma vez!)

### 3. Configurar Variável de Ambiente

```bash
# No terminal
export NUGET_API_KEY="sua_api_key_aqui"
```

### 4. Publicar os Pacotes

```bash
# Navegar para o diretório do projeto
cd /Users/devferreirag/NetThrottler

# Publicar cada pacote
dotnet nuget push artifacts/NetThrottler.Core.1.0.0.nupkg \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json

dotnet nuget push artifacts/NetThrottler.AspNetCore.1.0.0.nupkg \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json

dotnet nuget push artifacts/NetThrottler.Redis.1.0.0.nupkg \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### 5. Verificar Publicação

1. Acesse [nuget.org](https://www.nuget.org/)
2. Procure por "NetThrottler"
3. Verifique se os 3 pacotes aparecem
4. Teste a instalação:

```bash
# Criar projeto de teste
mkdir test-install && cd test-install
dotnet new console

# Instalar pacotes
dotnet add package NetThrottler.Core
dotnet add package NetThrottler.AspNetCore
dotnet add package NetThrottler.Redis
```

## 📋 Checklist de Publicação

- [ ] Conta criada no NuGet.org
- [ ] API Key gerada e configurada
- [ ] Build Release executado com sucesso
- [ ] Testes unitários passando (42/42)
- [ ] Pacotes gerados em `artifacts/`
- [ ] Documentação README.md completa
- [ ] Licença MIT incluída
- [ ] Tags e metadados corretos

## 🔄 Atualizações Futuras

Para publicar novas versões:

1. Atualize a versão nos arquivos `.csproj`:

   ```xml
   <Version>1.1.0</Version>
   ```

2. Execute o build e pack:

   ```bash
   dotnet build --configuration Release
   dotnet pack --configuration Release --output ./artifacts
   ```

3. Publique a nova versão:

   ```bash
   dotnet nuget push artifacts/NetThrottler.Core.1.1.0.nupkg \
     --api-key $NUGET_API_KEY \
     --source https://api.nuget.org/v3/index.json
   ```

## 🎯 Próximos Passos

Após a publicação:

1. **Criar GitHub Release** com changelog
2. **Implementar HttpClient Integration** (NetThrottler.HttpClient)
3. **Adicionar Monitoramento** (métricas e health checks)
4. **Criar Documentação Online** (GitHub Pages)
5. **Implementar Algoritmos Adicionais** (Leaky Bucket, Fixed Window)

## 📊 Métricas de Sucesso

- ✅ **42 testes unitários** passando
- ✅ **Zero erros de compilação**
- ✅ **3 pacotes** prontos para publicação
- ✅ **Documentação completa**
- ✅ **Exemplos funcionais**
- ✅ **CI/CD configurado**

---

**Status**: 🟢 Pronto para publicação no NuGet.org!

**Tempo estimado**: 15-30 minutos para publicação completa
