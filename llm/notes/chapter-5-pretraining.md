# Chapter 5: Pretraining on Unlabeled Data

## Summary
The "engine room" — teach the model **next-token prediction** by training on text.

### Core Pillars
- **Cross-Entropy Loss** — Measures "distance" between prediction and actual next word
- **Perplexity** — Human-readable metric; if perplexity=10, the model is as confused as choosing among 10 random words
- **AdamW Optimizer** — Updates weights based on calculated errors
- **Data Splitting** — 90% training / 10% validation to detect overfitting vs. real learning

## The Training Loop

```python
def train_model_simple(model, train_loader, val_loader, optimizer, device, num_epochs, ...):
    for epoch in range(num_epochs):
        model.train()
        for input_batch, target_batch in train_loader:
            optimizer.zero_grad()       # 1. Clear previous gradients
            loss = calc_loss_batch(...)  # 2. Forward pass → predictions → loss
            loss.backward()             # 3. Backward pass → calculate gradients
            optimizer.step()            # 4. Apply weight corrections
```

---

## Q&A

### What is a Tensor?
A specialized multi-dimensional array optimized for GPU hardware.
- Input IDs, weights ($W_q, W_k, W_v$), context vectors — all stored as tensors
- Enables **automatic differentiation** — PyTorch tracks how to adjust numbers to reduce loss

### Why does `torch.log` appear in loss calculation?
Cross-Entropy uses natural logarithm to convert probabilities into "penalty scores":
- High probability (close to 1) for correct word → loss ≈ **0**
- Low probability (close to 0) → loss = **large number** (major error signal)

### Why call `optimizer.zero_grad()` every batch?
PyTorch gradients **accumulate by default**. Without clearing them, the model would combine errors from current and previous batches → chaotic weight updates.

### What does the optimizer update?
It updates **weights (parameters)** — the permanent "brain settings" like $W_q$. It does **not** update temporary variables like context vectors, which are intermediate calculations in a single forward pass.
