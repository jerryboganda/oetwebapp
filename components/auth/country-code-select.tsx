"use client";

import React, { useMemo } from "react";
import Select, {
  components,
  type GroupBase,
  type OptionProps,
  type SingleValue,
  type SingleValueProps,
} from "react-select";
import * as flagComponents from "country-flag-icons/react/1x1";
import { COUNTRY_OPTIONS, type CountryEntry } from "@/lib/countries";

export type CountryCodeOption = CountryEntry;

/**
 * Backwards-compatible export. The full ISO 3166-1 country list is sourced
 * from `lib/countries.ts` and shared across web, desktop, and mobile.
 */
export const countryOptions: ReadonlyArray<CountryCodeOption> = COUNTRY_OPTIONS;

const fallbackCountryOption = countryOptions[0]!;

function CountryOption({
  option,
  compact = false,
}: {
  option: CountryCodeOption;
  compact?: boolean;
}) {
  const Flag = (flagComponents as Record<string, React.ComponentType | undefined>)[
    option.isoCode
  ];

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: compact ? "0.45rem" : "0.6rem",
      }}
    >
      <span
        style={{
          width: compact ? 20 : 22,
          height: compact ? 20 : 22,
          overflow: "hidden",
          borderRadius: "999px",
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          flexShrink: 0,
          background: Flag ? "transparent" : "rgba(123, 121, 255, 0.12)",
          color: "#3f45a5",
          fontSize: 10,
          fontWeight: 700,
        }}
        aria-hidden="true"
      >
        {Flag ? <Flag /> : <span>{option.isoCode}</span>}
      </span>
      <span style={{ fontWeight: 700 }}>{option.dialCode}</span>
      {!compact ? (
        <span
          style={{
            color: "#66708f",
            overflow: "hidden",
            whiteSpace: "nowrap",
            textOverflow: "ellipsis",
          }}
        >
          {option.label}
        </span>
      ) : null}
    </div>
  );
}

function CustomOption(props: OptionProps<CountryCodeOption, false, GroupBase<CountryCodeOption>>) {
  return (
    <components.Option {...props}>
      <CountryOption option={props.data} />
    </components.Option>
  );
}

function CustomSingleValue(props: SingleValueProps<CountryCodeOption, false, GroupBase<CountryCodeOption>>) {
  return (
    <components.SingleValue {...props}>
      <CountryOption option={props.data} compact />
    </components.SingleValue>
  );
}

interface CountryCodeSelectProps {
  value: string;
  onChange: (value: CountryCodeOption) => void;
  inputId?: string;
  ariaLabel?: string;
}

export default function CountryCodeSelect({
  value,
  onChange,
  inputId = "country-code-select",
  ariaLabel = "Country calling code",
}: CountryCodeSelectProps) {
  const selectedOption = useMemo(
    () =>
      countryOptions.find((option) => option.value === value) ??
      fallbackCountryOption,
    [value]
  );

  return (
    <Select<CountryCodeOption, false, GroupBase<CountryCodeOption>>
      inputId={inputId}
      instanceId={inputId}
      aria-label={ariaLabel}
      options={countryOptions}
      value={selectedOption}
      onChange={(option: SingleValue<CountryCodeOption>) => {
        if (option) {
          onChange(option);
        }
      }}
      isSearchable
      placeholder="Search country or +code"
      noOptionsMessage={() => "No country matches your search"}
      maxMenuHeight={320}
      menuPlacement="auto"
      classNamePrefix="auth-country-select"
      components={{
        IndicatorSeparator: null,
        Option: CustomOption,
        SingleValue: CustomSingleValue,
      }}
      filterOption={(candidate, input) => {
        const term = input.toLowerCase();
        const data = candidate.data;

        return (
          data.label.toLowerCase().includes(term) ||
          data.dialCode.toLowerCase().includes(term) ||
          data.isoCode.toLowerCase().includes(term)
        );
      }}
      styles={{
        control: (base, state) => ({
          ...base,
          minHeight: 54,
          width: 150,
          borderRadius: "1.1rem 0 0 1.1rem",
          borderColor: state.isFocused
            ? "rgba(123, 121, 255, 0.55)"
            : "rgba(117, 131, 178, 0.28)",
          boxShadow: state.isFocused
            ? "0 0 0 4px rgba(123, 121, 255, 0.14)"
            : "none",
          backgroundColor: "rgba(255, 255, 255, 0.85)",
          paddingLeft: 2,
        }),
        valueContainer: (base) => ({
          ...base,
          paddingLeft: 10,
          paddingRight: 8,
        }),
        dropdownIndicator: (base) => ({
          ...base,
          color: "#66708f",
          paddingLeft: 0,
        }),
        option: (base, state) => ({
          ...base,
          backgroundColor: state.isFocused
            ? "rgba(123, 121, 255, 0.08)"
            : "#ffffff",
          color: "#1e245c",
          cursor: "pointer",
        }),
        menu: (base) => ({
          ...base,
          zIndex: 30,
          borderRadius: 14,
          overflow: "hidden",
          minWidth: 280,
        }),
        menuList: (base) => ({
          ...base,
          maxHeight: 320,
        }),
      }}
    />
  );
}
