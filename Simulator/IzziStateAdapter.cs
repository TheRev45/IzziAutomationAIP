namespace IzziAutomationSimulator;

/// <summary>
/// Adaptador que converte SimulationState → formato Izzi.
/// 
/// Responsabilidades:
///   1. Mapear estados transitórios (LoggingIn, SettingUpQueue) → estáveis (LoggedOut, Idle)
///   2. Calcular RealCapacity de queues baseado em IzziDiscTime
///   3. Extrair informação relevante para decisão (omitir detalhes internos)
/// 
/// Design: Stateless (pura função de conversão)
/// </summary>
public class IzziStateAdapter
{
    private readonly SimulatorConfiguration _config;

    public IzziStateAdapter(SimulatorConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Converte estado completo da simulação para formato Izzi.
    /// 
    /// Returns: (recursos, queues) prontos para IzziCore.Run()
    /// </summary>
    public (List<UnpopulatedResource> resources, List<IzziQueue> queues) ConvertToIzziFormat(SimulationState state)
    {
        var resources = state.Resources.Values
            .Select(r => ConvertResource(r))
            .ToList();

        var queues = state.Queues.Values
            .Select(q => ConvertQueue(q, state))
            .ToList();

        return (resources, queues);
    }

    /// <summary>
    /// Converte um recurso para formato Izzi.
    /// 
    /// Mapeamento conservador de estados:
    ///   - LoggingIn → LoggedOut (assume que ainda não está pronto)
    ///   - LoggingOut → Idle (ainda tem user activo temporariamente)
    ///   - SettingUpQueue → Idle (quase pronto mas ainda não)
    ///   - Working → Working (mantém)
    ///   - Idle → Idle (mantém)
    ///   - LoggedOut → LoggedOut (mantém)
    /// 
    /// Izzi só vê: LoggedOut, Idle, Working
    /// </summary>
    private UnpopulatedResource ConvertResource(SimResourceState resource)
    {
        // Mapear estado transitório → estável
        string izzState = resource.CurrentState switch
        {
            SimResourceState.LoggingIn => SimResourceState.LoggedOut,  // conservador
            SimResourceState.LoggingOut => SimResourceState.Idle,      // ainda tem user
            SimResourceState.SettingUpQueue => SimResourceState.Idle,  // quase pronto
            _ => resource.CurrentState  // LoggedOut, Idle, Working mantêm
        };

        return new UnpopulatedResource(
            Id: resource.Id,
            State: izzState,
            CurrentUserId: resource.CurrentUserId,
            LoginTime: resource.AvgLoginTime,
            LogoutTime: resource.AvgLogoutTime
        );
    }

    /// <summary>
    /// Converte uma queue para formato Izzi.
    /// 
    /// Calcula RealCapacity baseado em IzziDiscTime:
    ///   RealCapacity = floor((DiscTime - SetupOverhead) / AvgItemDuration)
    /// 
    /// Exemplo:
    ///   DiscTime = 10 min
    ///   Setup = 2 min
    ///   AvgItem = 3 min
    ///   → RealCapacity = floor((10-2)/3) = 2 items podem ser processados nesta janela
    /// </summary>
    private IzziQueue ConvertQueue(SimQueueState queue, SimulationState state)
    {
        // Calcula média de duração de items baseada em histórico
        var avgItemDuration = queue.FinishedTasks.Any()
            ? TimeSpan.FromSeconds(queue.FinishedTasks.Average(t => t.DurationSeconds))
            : TimeSpan.FromMinutes(3); // fallback se sem histórico

        return new IzziQueue(
            Id: queue.Id,
            UserId: queue.UserId,
            PendingCount: queue.PendingItems.Count,
            SetupTime: queue.AvgSetupTime,
            AvgItemDuration: avgItemDuration,
            SLA: queue.SLA,
            Criticality: queue.Criticality
        );
    }

    /// <summary>
    /// Calcula RealCapacity de uma queue para a janela IzziDiscTime.
    /// 
    /// Formula:
    ///   RealCapacity = floor((DiscTime - SetupOverhead) / AvgItemDuration)
    /// 
    /// Este valor indica quantos items um recurso consegue processar dentro
    /// da janela de decisão da Izzi.
    /// 
    /// Usado pela Izzi para calcular benefício de alocar recurso a esta queue.
    /// </summary>
    public int CalculateRealCapacity(IzziQueue queue)
    {
        var availableTime = _config.IzziDiscTime - queue.SetupTime;
        
        if (availableTime <= TimeSpan.Zero)
            return 0;  // Setup maior que janela → 0 items processados

        var capacity = (int)Math.Floor(availableTime / queue.AvgItemDuration);
        
        return Math.Max(0, capacity);
    }

    /// <summary>
    /// Calcula benefício de alocar um recurso a uma queue.
    /// 
    /// Formula (exemplo simplificado):
    ///   Benefit = RealCapacity × Criticality × UrgencyMultiplier
    /// 
    /// Onde:
    ///   - RealCapacity: quantos items consegue processar
    ///   - Criticality: importância da queue (1-10)
    ///   - UrgencyMultiplier: >1 se próximo de SLA violation, =1 se normal
    /// 
    /// Usado pela Izzi para decidir qual queue priorizar.
    /// </summary>
    public double CalculateBenefit(IzziQueue queue, DateTimeOffset now)
    {
        var realCapacity = CalculateRealCapacity(queue);
        
        if (realCapacity == 0)
            return 0;

        // Urgency: itens próximos de SLA violation têm maior prioridade
        double urgencyMultiplier = 1.0;
        
        // Calcula tempo até próximo SLA violation
        // (Placeholder: em implementação real, percorrer PendingItems e ver SLADeadline)
        
        return realCapacity * queue.Criticality * urgencyMultiplier;
    }

    /// <summary>
    /// Valida se um comando é válido dado o estado actual do recurso.
    /// 
    /// Exemplos de validação:
    ///   - Login só é válido se recurso está LoggedOut
    ///   - StartProcess só é válido se recurso tem user activo
    ///   - Não pode alocar recurso com user A para queue de user B
    /// </summary>
    public bool ValidateCommand(object command, SimResourceState resource, SimulationState state)
    {
        switch (command)
        {
            case LoginCommand login:
                // Só pode fazer login se LoggedOut
                return resource.CurrentState == SimResourceState.LoggedOut;

            case LogoutCommand:
                // Só pode fazer logout se tem user
                return resource.CurrentUserId.HasValue;

            case StartProcessCommand start:
                var queue = state.Queues[start.QueueId];
                
                // Recurso precisa ter user correcto para a queue
                if (resource.CurrentUserId != queue.UserId)
                    return false;
                
                // Recurso precisa estar Idle
                if (resource.CurrentState != SimResourceState.Idle)
                    return false;

                return true;

            case StopProcessCommand:
                // Só pode parar se processo está ligado
                return resource.ProcessEnabled;

            default:
                return false;
        }
    }
}
