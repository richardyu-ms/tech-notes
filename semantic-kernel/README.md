# Semantic Kernel — Learning Notes

**Documentation**: [Introduction to Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/)

## Overview
Semantic Kernel is a lightweight, open-source development kit for building AI agents and integrating AI models into C#, Python, or Java applications. It serves as efficient middleware for enterprise-grade AI solutions.

## Key Concepts

- [Data Processing: Static vs. Dynamic](notes/01-data-and-memory.md) — How to feed context into LLM applications
- [Kernel vs. Agent Architecture](notes/03-kernel-vs-agent.md) — The relationship between the engine (Kernel) and the worker (Agent)

## Architecture at a Glance

```
User → Agent (stateful persona) → Kernel (stateless engine) → AI Services / Plugins / Memory
```

- **Kernel** — Manages AI services, memories, plugins. Stateless.
- **Agent** — Maintains conversation history, has a persona, uses the Kernel to act. Stateful.
