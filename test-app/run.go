package main

import (
	"fmt"
	"io"
	"net/http"
)

func handler(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "text/html")
	fmt.Fprintln(w, "<html><body style='font-family: monospace;'>")
	fmt.Fprintln(w, "<h2>Request Info & Headers</h2><pre>")
	// Special fields
	fmt.Fprintf(w, "Method: %s\n", r.Method)
	fmt.Fprintf(w, "URL: %s\n", r.URL.String())
	fmt.Fprintf(w, "Proto: %s\n", r.Proto)
	fmt.Fprintf(w, "Host: %s\n", r.Host)
	fmt.Fprintf(w, "RemoteAddr: %s\n", r.RemoteAddr)
	fmt.Fprintf(w, "ContentLength: %d\n", r.ContentLength)
	fmt.Fprintf(w, "TransferEncoding: %v\n", r.TransferEncoding)
	fmt.Fprintf(w, "Close: %v\n", r.Close)
	fmt.Fprintf(w, "RequestURI: %s\n", r.RequestURI)
	fmt.Fprintf(w, "TLS: %v\n", r.TLS != nil)
	fmt.Fprintln(w, "Headers:")
	for name, values := range r.Header {
		for _, value := range values {
			fmt.Fprintf(w, "%s: %s\n", name, value)
		}
	}
	fmt.Fprintln(w, "</pre>")
	fmt.Fprintln(w, "<h2>Body</h2><pre>")
	body, _ := io.ReadAll(r.Body)
	fmt.Fprintf(w, "%s", body)
	fmt.Fprintln(w, "</pre></body></html>")
}

func main() {
	http.HandleFunc("/", handler)
	fmt.Println("Listening on :8080")
	http.ListenAndServe(":8080", nil)
}
