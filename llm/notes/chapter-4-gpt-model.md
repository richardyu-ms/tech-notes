# Chapter 4: Implementing a GPT Model to Generate Text

## Summary
Assemble individual components into a complete GPT (Generative Pre-trained Transformer) architecture.

### Key Architecture Components

| Component | Purpose |
|-----------|---------|
| **Layer Normalization (4.2)** | Stabilizes training by normalizing inputs to mean=0, variance=1. GPT-2 uses "Pre-LayerNorm" (before attention/FFN) |
| **GELU Activation (4.3)** | Smoother alternative to ReLU; non-zero gradient for small negative values |
| **Feed-Forward Network (4.3)** | "Processing" and "knowledge storage" — expands embedding dim (4x) then contracts back |
| **Shortcut Connections (4.4)** | Skip connections to mitigate vanishing gradient in deep networks |
| **Transformer Block (4.5)** | Integrates LayerNorm + Multi-Head Attention + FFN |
| **GPT Model (4.6)** | Multiple Transformer Blocks + Linear output head → vocabulary logits |

### The Generation Loop
```
Input Text → Tokenizer (IDs) → Model (Logits) → Select Next ID → Append to Input → Repeat
```

---

## Q&A

### Why does the untrained model output "nonsense"?
The model "speaks" using a real dictionary (GPT-2 vocabulary: ~50,257 tokens). With random weights, it randomly picks words — like a baby pointing at random words in a dictionary.

### How does the tokenizer connect to the model config?
They are linked by **`vocab_size`**:
- `GPT_CONFIG_124M` defines `vocab_size = 50,257` — the model has that many "slots" in its Embedding and Output layers
- The `tiktoken` "gpt2" encoding generates IDs 0–50,256 matching those slots
- If these don't match → "index out of bounds" crash

### Where do model and tokenizer actually "meet" in code?
Inside the **`generate_text_simple`** function:
1. Tokenizer **encodes** text → numbers
2. Model processes numbers → prediction logits
3. Tokenizer **decodes** predicted number → human-readable word
