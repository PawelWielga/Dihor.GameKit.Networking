declare const process: { cwd(): string };

declare module "node:test" {
  interface TestContext {
    after(fn: () => void | Promise<void>): void;
  }

  type TestBody = (t: TestContext) => void | Promise<void>;
  export default function test(name: string, body: TestBody): void;
}

declare module "node:assert/strict" {
  interface Assert {
    equal(actual: unknown, expected: unknown, message?: string): void;
    deepEqual(actual: unknown, expected: unknown, message?: string): void;
    ok(value: unknown, message?: string): asserts value;
    throws(block: () => unknown, error?: RegExp, message?: string): void;
    rejects(
      block: () => Promise<unknown>,
      error?: RegExp,
      message?: string,
    ): Promise<void>;
  }
  const assert: Assert;
  export default assert;
}

declare module "node:fs" {
  export function readFileSync(path: string, encoding: "utf8"): string;
}

declare module "node:path" {
  export function resolve(...paths: string[]): string;
}
