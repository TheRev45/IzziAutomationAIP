# IzziCore — RPA Workforce Simulator

**Versão:** Standalone (Fevereiro 2026)  
**Status:** Arquitectura completa, pronta para integração

---

## 📋 O Que É Este Projeto?

**IzziCore** é um simulador de eventos discretos (DES) para workforce automation (RPA + Humanos + GenAI).

Foi desenvolvido como componente **standalone** antes da plataforma Izzi.Web, com arquitectura elegante e separação clara de responsabilidades.

---

## 🎯 Diferenças vs Izzi.Web

| Aspecto | IzziCore (Este Projeto) | Izzi.Web (POC Atual) |
|---------|-------------------------|----------------------|
| **Granularidade** | Eventos individuais por item | Contadores agregados (pending/processed) |
| **Autonomia** | Recursos pegam items automaticamente | Estados mudam aleatoriamente |
| **Comandos** | `StartProcess`, `StopProcess` (high-level) | N/A (simulação simplificada) |
| **Forecast** | Loop assíncrono isolado | Regras hard-coded simples |
| **Batch Processing** | Eventos simultâneos processados atomicamente | N/A |

---

## 🏗️ Arquitectura

```
┌─────────────────────────────────────────┐
│ IzziCore (Decision Engine)             │
│ ├─ Input: UnpopulatedResource[]        │
│ └─ Output: CommandsForResource[]       │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ Worker (Orchestrator)                   │
│ ├─ Chama Izzi a cada 10min ou trigger  │
│ ├─ Expande comandos em sequências      │
│ └─ Agenda eventos na EventQueue        │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ RealSimulator | ForecastSimulator       │
│ ├─ Processa eventos em batch           │
│ └─ Recursos operam autonomamente       │
└─────────────────────────────────────────┘
```

---

## 📦 Ficheiros Incluídos

### Core (7 ficheiros)
1. **SimulationClock.cs** — Relógio partilhado
2. **SimulatorConfiguration.cs** — Configuração global (record)
3. **EventQueue.cs** — Fila de eventos com batch processing
4. **SimulationEvents.cs** — LoginCompleted, LogoutCompleted, SetupCompleted, ItemCompleted
5. **SimulationState.cs** — Estado completo + DeepClone
6. **SimulatorEngine.cs** — Classe base (ProcessEvents, helpers)
7. **Worker.cs** — Observação + triggers + execução de comandos

### Simuladores (2 ficheiros)
8. **RealSimulator.cs** — Implementação para produção
9. **ForecastSimulator.cs** — Loop assíncrono de previsão

### Adaptadores (1 ficheiro)
10. **IzziStateAdapter.cs** — Conversão SimulationState → formato Izzi

### Documentação
11. **Simulador_RPA_Guia_Tecnico.docx** — Diagrama de classes + exemplos

---

## 🔑 Conceitos-Chave

### 1. Batch Processing
Eventos com o mesmo timestamp são processados **atomicamente** antes de qualquer observação externa.

```csharp
// Correcto: Worker vê R1=Idle e R2=Idle simultaneamente
06:03:25 → [ItemCompleted(R1), ItemCompleted(R2)]

// Errado: Worker vê estados intermédios inconsistentes
06:03:25 → ItemCompleted(R1)
Worker.Observe() → R1=Idle, R2=Working (ainda não processou!)
06:03:25 → ItemCompleted(R2)
```

### 2. Recursos Autónomos
Quando `ProcessEnabled=true`, recursos pegam items automaticamente **SEM** chamar Izzi:

```csharp
while (ProcessEnabled && queue.HasItems()) {
    item = queue.GetNext();
    ProcessItem(item);
}
// Queue vazia → CurrentState = Idle → trigger Worker → chama Izzi
```

### 3. Comandos High-Level
Izzi não micro-gerencia items individuais:

```csharp
// Izzi decide:
StartProcess(queueId)  // Liga processo → recurso trabalha autonomamente
StopProcess()          // Desliga após item atual
Login(user)
Logout
```

### 4. Forecast Assíncrono
ForecastSimulator corre em **Task.Run** separado:

```csharp
Task.Run(() => {
    var forecastSim = realSim.Clone();
    forecastSim.RunUntil(queuesEmpty || maxHorizon);
    return forecastSim.GeneratePredictions();
}, cancellationToken);
```

---

## 🚀 Como Usar

### Exemplo Básico

```csharp
// 1. Configuração
var config = SimulatorConfiguration.Demo; // 60× velocidade
config.Validate();

// 2. Estado inicial
var clock = new SimulationClock(DateTimeOffset.UtcNow);
var state = SimulationState.CreateInitial(resources, queues);
var eventQueue = new EventQueue();

// 3. Criar simulador
var realSim = new RealSimulator(clock, state, eventQueue, config);

// 4. Loop principal
while (realSim.CanAdvance())
{
    realSim.Step();
    
    // A cada 10min ou trigger Idle → chama Izzi
    // Forecast corre assíncrono em background
}

// 5. Resultados
var predictions = realSim.GetLatestForecast();
var gantt = realSim.ExportGanttData();
```

---

## 📊 Outputs

### Gantt Chart Data
```csharp
public record GanttSegment(
    Guid ResourceId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string State,  // "Working", "Idle", "LoggingIn", etc.
    Guid? QueueId,
    Guid? ItemId
);
```

### Forecast Predictions
```csharp
public record ForecastResult(
    DateTimeOffset GeneratedAt,
    DateTimeOffset HorizonEnd,
    Dictionary<Guid, DateTimeOffset> QueueCompletionETAs,
    List<Alert> Alerts,
    List<PrescriptiveAction> Recommendations
);
```

---

## ⚙️ Configurações

```csharp
// Produção (tempo real)
var config = SimulatorConfiguration.Default;

// Testes (máxima velocidade)
var config = SimulatorConfiguration.FastTest;

// Demo (60× acelerado)
var config = SimulatorConfiguration.Demo;

// Custom
var config = new SimulatorConfiguration
{
    Step = TimeSpan.FromSeconds(1),
    IzziTimerInterval = TimeSpan.FromMinutes(5),
    IzziDiscTime = TimeSpan.FromMinutes(5),
    ForecastHorizon = TimeSpan.FromHours(4),
    SpeedMultiplier = 120.0  // 2h simuladas em 1 minuto
};
```

---

## 🔗 Integração com Izzi.Web

Para integrar este IzziCore na plataforma Izzi.Web:

### Opção 1: Substituir SimulatorService
```csharp
// Izzi.Web/Services/SimulatorService.cs
public class SimulatorService : ISimulatorService
{
    private readonly RealSimulator _realSim;
    
    public SimulationState StartSimulation(string name, ConnectorData data)
    {
        // Converte ConnectorData → SimulationState
        // Inicia RealSimulator
    }
}
```

### Opção 2: Híbrido (ambos convivem)
```csharp
// Izzi.Web/Services/IzziCoreSimulatorService.cs (novo)
// Izzi.Web/Services/SimplifiedSimulatorService.cs (actual renomeado)

// Escolher qual usar via configuração
services.AddSingleton<ISimulatorService>(provider =>
{
    var useRealistic = Configuration.GetValue<bool>("UseIzziCore");
    return useRealistic 
        ? new IzziCoreSimulatorService(...)
        : new SimplifiedSimulatorService(...);
});
```

---

## 📚 Documentação Completa

Ver **Simulador_RPA_Guia_Tecnico.docx** para:
- Diagrama de classes completo
- Fluxo detalhado CallIzzi() passo-a-passo
- Exemplo 3 máquinas com timeline
- Threading e performance
- Checklist de integração

---

## ✅ Status

- ✅ Arquitectura completa e documentada
- ✅ 10 ficheiros C# (1.811 linhas de código)
- ✅ Batch processing implementado
- ✅ Forecast assíncrono thread-safe
- ✅ Recursos autónomos funcionais
- ✅ Worker completo (triggers + comandos)
- ✅ IzziStateAdapter completo (mapeamento conservador)
- ⏳ Pendente: IzziCore.Run() — algoritmo de optimização (stub presente)
- ⏳ Pendente: Integração com Izzi.Web

---

## 🎯 Próximos Passos Recomendados

1. **Implementar IzziCore.Run()** — algoritmo de decisão/optimização
2. **Integrar com Izzi.Web** — substituir ou conviver com SimulatorService actual
3. **Testes E2E** — cenário 3 máquinas completo
4. **Gantt Chart Rendering** — visualização timeline no frontend
5. **Performance tuning** — benchmark com 50+ recursos

---

## 👤 Autor

Desenvolvido em colaboração com Claude (Anthropic) — Fevereiro 2026

---

## 📝 Notas

Esta versão do IzziCore foi desenvolvida **antes** da plataforma Izzi.Web (Sprints 1-4) e representa a arquitectura "ideal" de simulação.

A Izzi.Web actual usa uma implementação **simplificada** (POC rápido) que é suficiente para demos mas não tem a granularidade e realismo do IzziCore.

A integração futura permitirá escolher entre:
- **Modo Demo** (actual) — rápido, visual, aproximado
- **Modo Realista** (IzziCore) — preciso, granular, production-ready
