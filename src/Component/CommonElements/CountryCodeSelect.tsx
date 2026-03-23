"use client";

import React, { useMemo } from "react";
import Select, { components } from "react-select";
import * as flagComponents from "country-flag-icons/react/1x1";

export interface CountryCodeOption {
  value: string;
  label: string;
  dialCode: string;
  isoCode: keyof typeof flagComponents;
}

export const countryOptions: CountryCodeOption[] = [
  { value: "pk", label: "Pakistan", dialCode: "+92", isoCode: "PK" },
  { value: "in", label: "India", dialCode: "+91", isoCode: "IN" },
  { value: "bd", label: "Bangladesh", dialCode: "+880", isoCode: "BD" },
  {
    value: "ae",
    label: "United Arab Emirates",
    dialCode: "+971",
    isoCode: "AE",
  },
  { value: "sa", label: "Saudi Arabia", dialCode: "+966", isoCode: "SA" },
  { value: "qa", label: "Qatar", dialCode: "+974", isoCode: "QA" },
  { value: "gb", label: "United Kingdom", dialCode: "+44", isoCode: "GB" },
  { value: "au", label: "Australia", dialCode: "+61", isoCode: "AU" },
  { value: "us", label: "United States", dialCode: "+1", isoCode: "US" },
  { value: "ca", label: "Canada", dialCode: "+1", isoCode: "CA" },
];
export const fallbackCountryOption: CountryCodeOption = {
  value: "pk",
  label: "Pakistan",
  dialCode: "+92",
  isoCode: "PK",
};

function CountryOption({
  option,
  compact = false,
}: {
  option: CountryCodeOption;
  compact?: boolean;
}) {
  const Flag = flagComponents[option.isoCode];

  return (
    <div className="d-flex align-items-center gap-2">
      <span
        className="rounded-circle overflow-hidden d-inline-flex align-items-center justify-content-center"
        style={{ width: compact ? 20 : 22, height: compact ? 20 : 22 }}
      >
        <Flag />
      </span>
      <span className="fw-medium">{option.dialCode}</span>
      {!compact ? (
        <span className="text-secondary text-truncate">{option.label}</span>
      ) : null}
    </div>
  );
}

const CustomOption = (props: any) => (
  <components.Option {...props}>
    <CountryOption option={props.data} />
  </components.Option>
);

const CustomSingleValue = (props: any) => (
  <components.SingleValue {...props}>
    <CountryOption option={props.data} compact />
  </components.SingleValue>
);

interface CountryCodeSelectProps {
  value: string;
  onChange: (value: CountryCodeOption) => void;
  inputId?: string;
}

export default function CountryCodeSelect({
  value,
  onChange,
  inputId = "country-code-select",
}: CountryCodeSelectProps) {
  const selectedOption: CountryCodeOption = useMemo(
    () =>
      countryOptions.find((option) => option.value === value) ??
      fallbackCountryOption,
    [value]
  );

  return (
    <Select<CountryCodeOption, false>
      inputId={inputId}
      instanceId={inputId}
      options={countryOptions}
      value={selectedOption}
      onChange={(option) => {
        if (option) {
          onChange(option as CountryCodeOption);
        }
      }}
      isSearchable
      classNamePrefix="auth-country-select"
      components={{
        IndicatorSeparator: null,
        Option: CustomOption,
        SingleValue: CustomSingleValue,
      }}
      filterOption={(candidate, input) => {
        const term = input.toLowerCase();
        const data = candidate.data as CountryCodeOption;
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
          borderRadius: "1.1rem 0 0 1.1rem",
          borderColor: state.isFocused
            ? "rgba(123, 121, 255, 0.55)"
            : "rgba(117, 131, 178, 0.28)",
          boxShadow: state.isFocused
            ? "0 0 0 4px rgba(123, 121, 255, 0.14)"
            : "none",
          width: 150,
          backgroundColor: "rgba(255, 255, 255, 0.85)",
        }),
        menu: (base) => ({
          ...base,
          zIndex: 30,
        }),
        valueContainer: (base) => ({
          ...base,
          paddingLeft: 10,
          paddingRight: 8,
        }),
        dropdownIndicator: (base) => ({
          ...base,
          paddingLeft: 0,
        }),
      }}
    />
  );
}
