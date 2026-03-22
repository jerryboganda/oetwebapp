import React, { useState } from "react";

interface PillRatingProps {
  options?: string[];
  initial?: string;
}

const PillRating: React.FC<PillRatingProps> = ({
  options = ["A", "B", "C", "D", "E", "F"],
  initial = "A",
}) => {
  const [selected, setSelected] = useState<string>(initial);
  const [hoveredOption, setHoveredOption] = useState<string | null>(null);

  const handleClick = (value: string) => {
    setSelected(value);
  };

  const isActive = (option: string) =>
    !hoveredOption && options.indexOf(option) <= options.indexOf(selected);

  const isHoverActive = (option: string) =>
    hoveredOption && options.indexOf(option) <= options.indexOf(hoveredOption);

  return (
    <div className="pill-container">
      {options.map((option) => (
        <div
          key={option}
          className={`pill-option ${
            isActive(option)
              ? "active"
              : isHoverActive(option)
                ? "hover-active"
                : ""
          }`}
          onClick={() => handleClick(option)}
          onMouseEnter={() => setHoveredOption(option)}
          onMouseLeave={() => setHoveredOption(null)}
        >
          {option}
        </div>
      ))}
    </div>
  );
};

export default PillRating;
