import React, { useState, useEffect } from "react";
import Select, { type ActionMeta, type MultiValue } from "react-select";

type Option = { value: string; label: string };

interface SelectProps {
  options: Option[];
  placeholder: string;
  defaultValue: string[];
  instanceId?: string;
}

const MultiSelect: React.FC<SelectProps> = ({
  options,
  placeholder,
  defaultValue,
  instanceId = "multi-select",
}) => {
  const [selectedOptions, setSelectedOptions] = useState<Option[]>([]);

  useEffect(() => {
    const newSelectedOptions = options.filter((option) =>
      defaultValue.includes(option.value)
    );

    if (
      JSON.stringify(newSelectedOptions) !== JSON.stringify(selectedOptions)
    ) {
      setSelectedOptions(newSelectedOptions);
    }
  }, [defaultValue, options, selectedOptions]);

  const handleChange = (
    selected: MultiValue<Option>,
    _actionMeta: ActionMeta<Option>
  ) => {
    setSelectedOptions([...selected]);
  };

  return (
    <Select
      instanceId={instanceId}
      isMulti
      options={options}
      value={selectedOptions}
      onChange={handleChange}
      placeholder={placeholder}
      classNamePrefix="custom-select"
    />
  );
};

export default MultiSelect;
