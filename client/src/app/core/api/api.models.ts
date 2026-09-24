/** Envelope every backend endpoint returns (Academies.BuildingBlocks.Application.Models.ApiResponse). */
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: Record<string, string[]> | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Best human-readable message from an HttpErrorResponse carrying an ApiResponse body. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  const body = (error as { error?: Partial<ApiResponse<unknown>> } | null)?.error;
  const firstFieldError = body?.errors ? Object.values(body.errors).flat()[0] : undefined;
  return firstFieldError ?? body?.message ?? fallback;
}
