# Chapter 2: Working with Text Data

## Summary
The core objective is to build a data preprocessing pipeline that transforms raw text into a numerical format that an LLM can understand.

### Pipeline Steps
1. **Tokenization** — Split raw text into smaller units (tokens): words or subwords
2. **Vocabulary Building** — Assign a unique integer ID to each unique token
3. **Special Tokens** — `<|unk|>` (unknown words) and `<|endoftext|>` (document boundaries)
4. **Byte Pair Encoding (BPE)** — Subword tokenization to handle large vocabularies and rare words efficiently
5. **Data Sampling (Sliding Window)** — Create input-target pairs by shifting a window across text
6. **Positional Encoding** — Add position information to embeddings so the model perceives word order

**Process Flow:**
```
Raw Text → Tokens → Token IDs → Token Embeddings + Positional Embeddings → Model Input
```

---

## Q&A

### What is the Embedding Layer?
An Embedding Layer is essentially a **lookup table**. It maps each unique integer ID to a continuous, high-dimensional numerical vector.

- **From Discrete to Continuous** — Moves tokens from arbitrary IDs to a mathematical space where backpropagation works
- **Capturing Semantics** — Words with similar meanings (e.g., "cat" and "dog") end up closer together
- **Trainable Weights** — Vectors improve as the model learns

### Are Token Embeddings responsible for word order?
**No.** Token Embeddings are order-agnostic — the vector for word "A" is the same regardless of position. The **Positional Embedding** provides the "where" information.

When you initialize `nn.Embedding` in PyTorch, vectors are randomly generated. During training, the model adjusts these numbers whenever it makes a wrong prediction. After massive training, random numbers evolve into a "semantic space."
