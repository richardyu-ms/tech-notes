# Data Processing in Semantic Kernel: Static vs. Dynamic

## 1. Static vs. Dynamic Data

### Static Data (Pre-baked)
- **Definition**: Data that doesn't change frequently or is hardcoded into the prompt
- **Examples**: System instructions, persona definitions, few-shot examples
- **Location**: Directly inside the prompt string

### Dynamic Data (Episodic/Semantic)
- **Definition**: Information that changes, is too large to fit in a prompt, or needs to be queried
- **Pre-processing**:
  1. **Chunking** — Split text into manageable pieces
  2. **Embedding** — Convert text into vectors using an embedding model
  3. **Storing** — Save vectors in a vector database (Azure AI Search, Chroma, Qdrant)
- **Usage**: Retrieved at runtime using "semantic search" (finding text with similar meaning)

## 2. Data Retrieval Patterns

### Pattern A: Direct Knowledge Retrieval (RAG)
```
User: "What is the company vacation policy?"
  → Calculate embedding of question
  → Query vector DB → top 3 relevant paragraphs
  → Insert into prompt → Send to LLM
  → Natural language answer
```

### Pattern B: Native Functions as Data Fetchers
```
User: "What is the status of order #123?"
  → Kernel calls Native Function GetOrderStatus(123)
  → Function queries SQL DB → returns "Shipped"
  → LLM generates natural response
```

### Sample: RAG with Memory

```python
from semantic_kernel.memory import VolatileMemoryStore, SemanticTextMemory

# 1. Initialize memory
memory_store = VolatileMemoryStore()
semantic_memory = SemanticTextMemory(storage=memory_store, embeddings_generator=embedding_gen)

# 2. Pre-processing (ingestion)
await semantic_memory.save_information(
    collection="policies", id="info1",
    text="Company vacation policy allows 20 days per year."
)

# 3. Retrieval (dynamic phase)
results = await semantic_memory.search("policies", "How many holidays do I get?", limit=1)
```
