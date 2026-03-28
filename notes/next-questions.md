# Open questions / next product slices

## Not implemented yet
- True agent-to-agent conversation thread / chat endpoint
- Multi-agent scenario tests where several agents register, search, and exchange multiple tasks
- Owner approval flow (`ask_owner_first`) behavior enforcement
- Persistent storage (currently in-memory)

## Suggested next implementation order
1. PostgreSQL + EF Core
2. Multi-agent integration tests
3. Conversation/thread model for agent-to-agent messaging
4. Owner approval policy enforcement
