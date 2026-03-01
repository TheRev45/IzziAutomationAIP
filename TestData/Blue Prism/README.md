# Blue Prism Test Dataset — IzziAutomationAIP

Simulated Blue Prism export for a production day (2024-03-01, 08:00–10:00).
5 bots, 8 queues, 159 work items across two load waves.

## Files

| File | Description |
|------|-------------|
| BPAResource.csv | 5 bots (BP-BOT-01 to BP-BOT-05), all Idle at 07:59 |
| BPAWorkQueue.csv | 8 queues (Queue_A to Queue_H) |
| BPAWorkQueueItem.csv | 159 items with loaded/finished/worktime timestamps |
| BPASession.csv | 13 sessions recording bot activity per queue |
| izzi_queue_config.csv | Izzi configuration — maps BP queues to Izzi parameters |

## Load Pattern

| Wave | Time | Items loaded |
|------|------|-------------|
| Wave 1 | 08:00 | 114 items across all 8 queues |
| Wave 2 | 08:30 | 45 items across all 8 queues |

## Queue Summary

| Queue | BP Process | Criticality | SLA | Avg Item Time | Wave1 | Wave2 |
|-------|-----------|-------------|-----|---------------|-------|-------|
| Queue_A | Process_A | 5 | 30 min | 60s | 25 | 10 |
| Queue_B | Process_B | 5 | 45 min | 120s | 20 | 8 |
| Queue_C | Process_C | 4 | 60 min | 180s | 15 | 6 |
| Queue_D | Process_D | 4 | 60 min | 120s | 18 | 7 |
| Queue_E | Process_E | 3 | 90 min | 240s | 10 | 5 |
| Queue_F | Process_F | 3 | 120 min | 180s | 12 | 4 |
| Queue_G | Process_G | 2 | 120 min | 300s | 8 | 3 |
| Queue_H | Process_H | 1 | 180 min | 360s | 6 | 2 |

## How Izzi Parameters Were Derived

### Derived from Blue Prism data (automatic in future connector)
- **avg_item_time_seconds** → `AVG(worktime)` per queue from BPAWorkQueueItem
- **avg_login_time_seconds** → `MIN(session.startdatetime - previous_session.enddatetime)` per bot, where a logout occurred between sessions
- **avg_logout_time_seconds** → `MIN(next_session.startdatetime - session.enddatetime)` per bot, where a logout occurred between sessions
- **avg_setup_time_seconds** → `AVG(first_item.finished - first_item.worktime - session.startdatetime)` per session, for items that were new (not retried)

### Requires human input (cannot be derived)
- **criticality** — business priority defined by the client
- **sla_minutes** — SLA targets defined by the client
- **izzi_user** — which Izzi user credential maps to each BP process
- **min_resources, max_resources, must_run, force_max** — capacity constraints defined by the client
