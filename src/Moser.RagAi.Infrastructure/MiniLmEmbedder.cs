using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Moser.RagAi;

public sealed class MiniLmEmbedder : IEmbeddingGenerator, IDisposable
{
    public const int Dimensions = 384;
    private const int MaxTokens = 256;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly int? _fixedLength;
    private readonly object _gate = new();

    public MiniLmEmbedder(string modelPath, string vocabPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(vocabPath);
        if (_session.InputMetadata.TryGetValue("input_ids", out var input) && input.Dimensions.Length == 2 && input.Dimensions[1] > 0)
            _fixedLength = input.Dimensions[1];
    }

    public int Dimension => Dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Embed(string.IsNullOrWhiteSpace(text) ? "." : text));
    }

    public void Dispose() => _session.Dispose();

    private float[] Embed(string text)
    {
        var budget = _fixedLength ?? MaxTokens;
        var ids = _tokenizer.EncodeToIds(text, budget, addSpecialTokens: true, out _, out _);
        var length = _fixedLength ?? Math.Max(1, ids.Count);
        var tokens = new long[length];
        var mask = new long[length];
        var count = Math.Min(ids.Count, length);
        for (var i = 0; i < count; i++)
        {
            tokens[i] = ids[i];
            mask[i] = 1;
        }

        var inputs = new List<NamedOnnxValue>(3);
        Add(inputs, "input_ids", tokens);
        Add(inputs, "attention_mask", mask);
        if (_session.InputMetadata.ContainsKey("token_type_ids"))
            Add(inputs, "token_type_ids", new long[length]);

        lock (_gate)
        {
            using var results = _session.Run(inputs);
            return Pool(results.First().AsTensor<float>(), mask);
        }
    }

    private void Add(List<NamedOnnxValue> inputs, string name, long[] values)
    {
        var metadata = _session.InputMetadata[name];
        var length = values.Length;
        if (metadata.ElementDataType == TensorElementType.Int32)
        {
            var ints = new int[length];
            for (var i = 0; i < length; i++)
                ints[i] = (int)values[i];
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(ints, [1, length])));
            return;
        }

        inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, [1, length])));
    }

    private static float[] Pool(Tensor<float> tensor, long[] mask)
    {
        if (tensor.Dimensions.Length == 2)
        {
            var ready = new float[Dimensions];
            for (var d = 0; d < Dimensions; d++)
                ready[d] = tensor[0, d];
            VectorMath.Normalize(ready);
            return ready;
        }

        var sequence = tensor.Dimensions[1];
        var width = tensor.Dimensions[2];
        if (width != Dimensions)
            throw new InvalidOperationException($"The embedding model returned {width} dimensions.");

        var sum = new float[width];
        var used = 0;
        for (var token = 0; token < sequence && token < mask.Length; token++)
        {
            if (mask[token] == 0)
                continue;
            used++;
            for (var d = 0; d < width; d++)
                sum[d] += tensor[0, token, d];
        }

        if (used > 0)
        {
            for (var d = 0; d < width; d++)
                sum[d] /= used;
        }

        VectorMath.Normalize(sum);
        return sum;
    }
}
