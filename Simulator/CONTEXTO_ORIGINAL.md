# CONTEXTO ORIGINAL — IzziCore Simulator

**Data:** Fevereiro 2026  
**Sessão:** Desenvolvimento standalone do simulador antes da plataforma Izzi.Web

---

## 🎯 PROBLEMA A RESOLVER

Criar um simulador de workforce automation que:

1. **Modele RPA resources com precisão**
   - Estados: LoggedOut, LoggingIn, Idle, SettingUpQueue, Working, LoggingOut
   - Transições com durações reais (login: ~60s, setup: ~30-45s, items: variável)
   - Recursos autónomos que pegam items sem micro-gestão

2. **Integre com motor de decisão (Izzi)**
   - Izzi é **stateless** — recebe estado → devolve comandos
   - Worker chama Izzi em triggers:
     * A cada 10 minutos (timer)
     * Quando recurso fica Idle sem work
   - Comandos são **high-level**: StartProcess, StopProcess, Login, Logout

3. **Suporte dois modos**
   - **Real Simulator**: processa eventos históricos + tempo real
   - **Forecast Simulator**: projecta futuro (8h horizon) de forma assíncrona

4. **Gere Gantt Chart data**
   - Timeline completa de cada recurso
   - Segmentos: (timestamp_start, timestamp_end, state, queue, item)

---

## 🏗️ DECISÕES ARQUITECTURAIS CRÍTICAS

### 1. Batch Processing de Eventos

**Problema:**
```
Se R1 e R2 completam items ao mesmo tempo (06:03:25):
- Processar R1 primeiro → Worker observa R1=Idle, R2=Working (errado!)
- Worker chama Izzi com estado inconsistente
```

**Solução:**
```csharp
// EventQueue agrupa eventos por timestamp
06:03:25 → [ItemCompleted(R1), ItemCompleted(R2)]

// Processa batch completo ANTES de Worker.Observe()
foreach (var evt in batch) evt.Apply(state, eventQueue);

// Agora Worker vê: R1=Idle E R2=Idle simultaneamente ✓
```

---

### 2. Recursos Autónomos (Não Micro-Geridos)

**Problema inicial:**
```
Izzi decidia item-a-item:
ExecuteQueue → ExecuteItem(A01) → ExecuteItem(A02) → ...
```

**Solução adoptada:**
```csharp
// Izzi só LIGA ou DESLIGA processos
StartProcess(queueId) → ProcessEnabled = true

// Recurso trabalha autonomamente:
while (ProcessEnabled && queue.HasItems()) {
    item = queue.GetNext();
    ProcessItem(item);
    // Agenda ItemCompletedEvent automaticamente
}

// Quando queue vazia → Idle → trigger Worker → chama Izzi
```

**Benefício:** Reduz chamadas à Izzi de centenas (por item) para dezenas (por mudança de processo).

---

### 3. Mapeamento Conservador para Izzi

**Problema:**
```
Recursos em estados transitórios (LoggingIn, SettingUpQueue):
- Izzi não sabe lidar com estes estados
- Mas são temporários e previsíveis
```

**Solução:**
```csharp
// IzziStateAdapter mapeia estados transitórios para estáveis
LoggingIn → LoggedOut (conservador - assume não está pronto)
LoggingOut → Idle (ainda tem user activo temporariamente)
SettingUpQueue → Idle (quase pronto mas não ainda)

// Izzi só vê: LoggedOut, Idle, Working
// Decisões são conservadoras mas seguras
```

---

### 4. Forecast Assíncrono

**Problema:**
```
Forecast pode demorar segundos (simula 8h de futuro)
Bloquear o RealSimulator é inaceitável
```

**Solução:**
```csharp
// ForecastWorker executa em Task.Run separado
Task.Run(() => {
    var forecastSim = realSim.Clone();  // Deep clone
    forecastSim.RunUntil(queuesEmpty || 8h);
    return forecastSim.GeneratePredictions();
}, cancellationToken);

// RealSimulator continua a correr
// Forecast actualiza quando completa (thread-safe com lock)
```

---

### 5. Request Stop Passivo

**Problema:**
```
StopProcess deve parar recurso, mas:
- Não podemos interromper item a meio
- Item pode estar a 50% de conclusão
```

**Solução:**
```csharp
// StopProcess apenas regista intenção
resource.RequestStopAt = clock.Now;

// ItemCompletedEvent verifica:
if (RequestStopAt != null) {
    ProcessEnabled = false;  // Não pega próximo item
    CurrentState = Idle;
}

// Para Gantt: marca segmento como "RequestStop" mesmo que Working
// Visualmente: cor diferente mostra que vai parar
```

---

## 📐 DESIGN PATTERNS USADOS

1. **Event Sourcing**
   - Estado deriva de eventos: `state.Apply(event)`
   - Reproduzível: mesmos eventos → mesmo estado

2. **Command Pattern**
   - Comandos encapsulam acções: `Login(user)`, `StartProcess(queue)`
   - Worker expande comandos em sequências temporais

3. **Strategy Pattern**
   - `RealSimulator` vs `ForecastSimulator` partilham `SimulatorEngine`
   - Diferem apenas em condição de paragem

4. **Shared State (Clock)**
   - Relógio partilhado por referência (não clonado excepto Forecast)
   - Single source of truth para tempo

5. **Deep Clone (Immutability)**
   - Forecast clona estado completo para não interferir com Real
   - Records ajudam: `state with { ... }`

---

## 🎓 LIÇÕES APRENDIDAS

### 1. Granularidade Temporal Importa
```
Step = 1 segundo → preciso mas lento
Step = 5 segundos → mais rápido mas pode perder eventos

Solução: Step configurável, default 1s
```

### 2. Batch Processing É Essencial
```
Sem batch → estados inconsistentes observados pelo Worker
Com batch → atomicidade garante consistência
```

### 3. Autonomia Reduz Complexidade
```
Micro-gestão: Izzi decide cada item → centenas de decisões
Autonomia: Izzi decide processos → dezenas de decisões
```

### 4. Threading Requer Cuidado
```
Forecast assíncrono → precisa lock em LatestForecast
CancellationToken → permite parar Forecast se demorar muito
```

---

## 📊 MÉTRICAS DE COMPLEXIDADE

```
Linhas de código: ~2.500
Ficheiros: 10 (.cs) + 1 (.docx)
Classes principais: 15
Eventos: 4 tipos (Login/Logout/Setup/ItemCompleted)
Estados recurso: 6
Comandos Izzi: 4
```

---

## 🔗 INTEGRAÇÃO FUTURA

### Com Izzi.Web (Plataforma)

**IzziCore** é standalone e **não depende** de Izzi.Web.

A integração seria:

```
IzziCore (este projeto)
    ↓ via adapter
Izzi.Web/Services/IzziCoreSimulatorService.cs
    ↓ implementa
Izzi.Web/Services/ISimulatorService.cs (interface)
    ↓ usado por
Izzi.Web/Controllers/SimulationController.cs
```

**Vantagem:** Izzi.Web pode ter **dois** simuladores:
- SimplifiedSimulator (actual) — demo rápida
- IzziCoreSimulator (este) — produção realista

---

## ⚠️ PENDÊNCIAS (Fora de Scope Original)

1. **IzziCore.Run() — Decision Engine**
   - Interface definida: `CommandsForResource[] Run(UnpopulatedResource[], IzziQueue[])`
   - Implementação: algoritmo de optimização TBD
   - Opções: Greedy, LP (Linear Programming), Genetic Algorithm

2. **Persistência**
   - Estado actual: in-memory apenas
   - Futura: guardar snapshots em DB para replay

3. **Multi-Tenancy**
   - Actual: single simulation
   - Futura: múltiplas simulações isoladas

4. **Real-Time Sync**
   - Actual: standalone
   - Futura: sync com RPA systems reais (BluePrism API, UiPath Orchestrator)

---

## 🎯 OBJECTIVO FINAL

Ter um simulador que:

✅ Modela workforce com precisão ao segundo  
✅ Suporta RPA + Humanos + GenAI  
✅ Gera forecasts realistas (8h horizon)  
✅ Integra com motor de decisão (Izzi)  
✅ Exporta Gantt charts para visualização  
✅ Corre assíncrono (Real + Forecast paralelos)  
✅ É configurável (Demo 60× vs Produção 1×)  

**Status:** ✅ Arquitectura completa e funcional

---

## 📚 REFERÊNCIAS

- **Discrete Event Simulation (DES):** Cassandras & Lafortune (2008)
- **Event Sourcing:** Fowler (2005)
- **Workforce Optimization:** Pinedo "Scheduling: Theory, Algorithms and Systems" (2016)

---

## 🙏 AGRADECIMENTOS

Desenvolvido em parceria com Claude (Anthropic) através de múltiplas sessões iterativas:
- Sessão 1: Definição de requisitos e arquitectura
- Sessão 2: Refinamento de triggers e comandos
- Sessão 3: Batch processing e autonomia
- Sessão 4: Forecast assíncrono
- Sessão 5: Código final e documentação

Total: ~15 horas de design + desenvolvimento + documentação

---

**Fim do Contexto Original**
