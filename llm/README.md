# LLM from Scratch — Reading Notes

Notes from **"Build a Large Language Model (From Scratch)"** by Sebastian Raschka.

## Chapter Notes

- [Chapter 2: Working with Text Data](notes/chapter-2-text-data.md) — Tokenization, BPE, embeddings, positional encoding
- [Chapter 3: Attention Mechanisms](notes/chapter-3-attention.md) — Self-attention, causal attention, multi-head attention
- [Chapter 4: Implementing a GPT Model](notes/chapter-4-gpt-model.md) — LayerNorm, GELU, Transformer blocks, text generation
- [Chapter 5: Pretraining on Unlabeled Data](notes/chapter-5-pretraining.md) — Cross-entropy loss, perplexity, training loop

## Key Takeaways

The book builds a GPT-like model step by step:

```
Raw Text → Tokenizer → Token IDs → Embeddings → Transformer Blocks → Output Logits → Next Token
```

Each chapter adds one piece of the puzzle:
1. **Ch 2** — How to convert text into numbers (tokenization + embeddings)
2. **Ch 3** — How tokens "talk" to each other (attention)
3. **Ch 4** — How to stack everything into a full model (GPT architecture)
4. **Ch 5** — How to teach the model to predict the next word (training loop)
