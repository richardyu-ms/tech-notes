# Chapter 3: Coding Attention Mechanisms

## Summary
Chapter 3 introduces the "soul" of the Transformer: the **Attention Mechanism**. It allows the model to focus on different parts of a sequence to create context-aware representations.

### Three Stages of Evolution
1. **Simple Self-Attention (3.3)** — Dot products to calculate similarity; create weighted sums
2. **Causal Attention (3.5)** — The "GPT Core": adds a mask to hide future tokens, ensuring next-word prediction only uses previous context
3. **Multi-Head Attention (3.6)** — Industrial-grade: splits query into multiple "heads" (parallel experts) that look at grammar, logic, etc. simultaneously

**Key takeaway:** Moves the model from "static" word meanings to "dynamic" context.

---

## Q&A

### What is the attention flow?
```
Input → Attention Scores (dot product) → Attention Weights (softmax) → Context Vectors (weighted sum)
```

- **Attention Scores** — Measuring relevance via dot product
- **Attention Weights** — Normalizing scores via softmax (probabilities summing to 1)
- **Context Vectors** — A vector that fuses information from neighbors based on weights

### Why introduce Q, K, V weight matrices?
In the simplified version, the model just averages vectors. With **Query**, **Key**, **Value** matrices:
- **Query** — "What am I looking for?"
- **Key** — "What information do I contain?"
- **Value** — "What content do I actually contribute?"

This makes attention **trainable** — the model learns which relationships matter.

### How does 3.4 translate theory into PyTorch?
- Uses `nn.Parameter` to define $W_q, W_k, W_v$ so PyTorch tracks gradients
- Introduces **Scaled Dot-Product Attention** (dividing by $\sqrt{d_k}$) to prevent vanishing gradients during softmax
