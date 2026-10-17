FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Moser.RagAi.Web/Moser.RagAi.Web.csproj --configuration Release --output /out
RUN mkdir -p /models \
    && curl -fsSL -A Moser.RagAi -o /models/model.onnx https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx \
    && curl -fsSL -A Moser.RagAi -o /models/vocab.txt https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
COPY --from=build /models ./models
ENV ASPNETCORE_URLS=http://+:8080
ENV Embeddings__Directory=/app/models
EXPOSE 8080
ENTRYPOINT ["dotnet", "Moser.RagAi.Web.dll"]
