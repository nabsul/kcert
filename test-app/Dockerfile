# Simple Dockerfile for Go web app
FROM golang:alpine AS builder
WORKDIR /app
COPY . .
RUN go build -ldflags="-s -w" -o app run.go

# Use distroless for minimal and secure runtime
FROM gcr.io/distroless/static-debian12:nonroot
WORKDIR /app
COPY --from=builder /app/app .
EXPOSE 8080
USER nonroot
CMD ["/app/app"]
