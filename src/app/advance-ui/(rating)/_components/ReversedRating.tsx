import React, { useState } from "react";

interface TextRatingProps {
  options?: string[];
  initial?: number;
}

const ReversedRating: React.FC<TextRatingProps> = ({
  options = [
    "Strongly Disagree",
    "Disagree",
    "Neither Agree nor Disagree",
    "Agree",
    "Strongly Agree",
  ],
  initial = 2,
}) => {
  const [selected, setSelected] = useState<number>(initial);
  const [hovered, setHovered] = useState<number | null>(null);

  const handleClick = (index: number) => {
    setSelected(index);
  };

  const getLabel = () => {
    if (hovered !== null) return options[hovered];
    return options[selected];
  };

  return (
    <div className="rating-wrapper">
      <div className="rating-boxes">
        {[0, 1, 2, 3, 4].map((index) => {
          const reversedIndex = 4 - index;
          const activeIndex = hovered !== null ? hovered : selected;
          const isFilled = reversedIndex <= activeIndex;

          return (
            <div
              key={index}
              className={`rating-pill ${isFilled ? "filled" : ""}`}
              onClick={() => handleClick(reversedIndex)}
              onMouseEnter={() => setHovered(reversedIndex)}
              onMouseLeave={() => setHovered(null)}
            ></div>
          );
        })}
      </div>

      <div className="rating-label">{getLabel()}</div>
    </div>
  );
};

export default ReversedRating;
