// Liveness контейнера web (Docker healthcheck). Не зависит от API, чтобы не каскадировать сбои.
export function GET() {
  return Response.json({ status: "Healthy" });
}
