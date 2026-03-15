# Architecture: Kernel vs. Agent

## 1. The Kernel
- **Role**: The Engine / The Utility Belt
- **Responsibility**:
  - Manages connections to AI services (OpenAI, Azure, Hugging Face)
  - Manages Memories (Vector DBs)
  - Manages Plugins & Functions (Tool registry)
  - Handles low-level execution (prompt rendering, network calls, retries)
- **State**: Generally **stateless** — takes input, runs function, gives output

## 2. The Agent
- **Role**: The Persona / The Worker
- **Responsibility**:
  - Maintains **history/state** (conversation so far)
  - Has a specific "job" or "persona"
  - **Uses the Kernel** to perform tasks
  - Can loop autonomously (Think-Act-Observe cycles)
- **State**: **Stateful** — remembers conversation turns

## 3. How They Work Together

The Agent **owns** a Kernel instance. When you ask the Agent to do something:
1. Looks at its history
2. Decides what to do
3. Asks the **Kernel** to execute specific Functions
4. Updates its history with the result

### Sample: Agent Loop

```python
from semantic_kernel.agents import ChatCompletionAgent
from semantic_kernel.contents import ChatHistory

# 1. Setup Kernel (the Toolbelt)
kernel = Kernel()
kernel.add_service(AzureChatCompletion(...))

# 2. Setup Agent (the Worker)
agent = ChatCompletionAgent(
    service_id="chat", kernel=kernel, name="CodeHelper",
    instructions="You are an expert programmer. Check code for errors."
)

# 3. Interaction Loop
chat_history = ChatHistory()
chat_history.add_user_message("I have a bug in my python script.")

async for content in agent.invoke(chat_history):
    print(f"Agent: {content.content}")
```

## Summary
- **Kernel**: "I know how to call the API and run tools." (Capability)
- **Agent**: "I know who I am, what we discussed, and which tools to use." (Behavior)
