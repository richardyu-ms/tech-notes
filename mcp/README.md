# MCP (Model Context Protocol) — Learning Notes

Notes from prototyping with MCP and building AI agent tools.

## Key Documents

- [Agent vs. MCP: Competitors or Partners?](notes/agent-vs-mcp.md) — Understanding the relationship between AI agents and MCP

## What is MCP?

MCP is a **Client-Server protocol** that standardizes how AI agents connect to external tools and data sources.

```
Agent (Brain) ←→ MCP Protocol (Interface) ←→ Tools/Data
```

- **Agent** = The "brain" that decides what to do
- **MCP** = The standardized "interface" for connecting to tools
- **MCP Server** = A process that exposes tools via the protocol

### The USB Keyboard Analogy
- **MCP Server = USB Keyboard** — Works on Windows, Mac, Linux. Doesn't care about the OS.
- **Agent = The Operating System** — Different platforms, but all can plug into the same keyboard.

MCP frees your tools from being locked into one specific agent framework.
