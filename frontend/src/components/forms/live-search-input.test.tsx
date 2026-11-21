import { describe, expect, it } from "vitest";
import { searchUrl, shouldSearch } from "./live-search-input";

describe("live search", () => {
  it("searches from two characters, and an empty field resets the search", () => {
    expect(shouldSearch("", 2)).toBe(true);
    expect(shouldSearch("а", 2)).toBe(false);
    expect(shouldSearch(" аи ", 2)).toBe(true);
  });

  it("keeps the other filters of the form and drops the page number", () => {
    const form = new FormData();
    form.set("status", "Confirmed");
    form.set("from", "2026-11-21");
    form.set("kind", "");
    form.set("page", "3");
    form.set("search", "старое");
    expect(searchUrl("/admin/bookings", form, "search", "Айгерим")).toBe(
      "/admin/bookings?status=Confirmed&from=2026-11-21&search=%D0%90%D0%B9%D0%B3%D0%B5%D1%80%D0%B8%D0%BC",
    );
  });

  it("returns the bare path when nothing is set", () => {
    expect(searchUrl("/admin/guests", new FormData(), "search", "  ")).toBe("/admin/guests");
  });
});
