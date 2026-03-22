import React, { useState } from "react";

interface BarRatingProps {
  max?: number;
  initial?: number;
  barColor?: string;
}

const HorizontalRating: React.FC<BarRatingProps> = ({
  max = 10,
  initial = 1,
}) => {
  const [rating, setRating] = useState<number>(initial);
  const [hovered, setHovered] = useState<number | null>(null);

  const handleClick = (value: number) => {
    setRating(value);
  };

  return (
    <div className="hr-wrapper">
      <div className="hr-bar-stack">
        {Array.from({ length: max }, (_, i) => {
          const index = i + 1;
          const isHovered = hovered !== null ? index <= hovered : false;
          const isFilled = index <= rating;
          const shouldFill = isHovered || isFilled;

          return (
            <div
              key={index}
              className={`hr-bar ${shouldFill ? "filled" : ""}`}
              onClick={() => handleClick(index)}
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
            />
          );
        })}
      </div>
      <div className="hr-value">{rating}</div>
    </div>
  );
};

export default HorizontalRating;
