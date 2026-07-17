import { parsePairingKey } from "./ilxr.mjs";

const ENVIRONMENT_NAME = "ILXR_PAIRING_KEY";

/**
 * Consume a pairing key without retaining the environment copy. `--key` is a
 * loopback-only convenience for the public test vector; private/non-loopback
 * sessions must avoid exposing a key in argv and shell history.
 */
export function consumePairingKey({ argumentValue, environment = process.env, requireEnvironment = false }) {
  const hasEnvironmentValue = Object.hasOwn(environment, ENVIRONMENT_NAME);
  const environmentValue = hasEnvironmentValue ? environment[ENVIRONMENT_NAME] : undefined;
  if (hasEnvironmentValue) delete environment[ENVIRONMENT_NAME];

  if (argumentValue !== undefined && environmentValue !== undefined) {
    throw new Error(`provide either --key or ${ENVIRONMENT_NAME}, not both`);
  }
  if (requireEnvironment && argumentValue !== undefined) {
    throw new Error(`non-loopback use requires ${ENVIRONMENT_NAME}; --key is loopback-only`);
  }

  const selected = environmentValue ?? argumentValue;
  if (selected === undefined) {
    throw new Error(`pairing key is required in ${ENVIRONMENT_NAME} (or --key for public loopback tests)`);
  }
  return parsePairingKey(selected);
}

export { ENVIRONMENT_NAME };
