import React, { useState } from "react";
interface RatingBarProps {
  max?: number;
  initial?: number;
}

const RatingBar: React.FC<RatingBarProps> = ({ max = 10, initial = 1 }) => {
  const [rating, setRating] = useState<number>(initial);
  const [hovered, setHovered] = useState<number | null>(null);

  const handleClick = (value: number) => {
    setRating(value);
  };

  // Display hovered value if hovering, else the saved rating
  const displayValue = hovered !== null ? hovered : rating;

  return (
    <div className="rating-wrapper">
      <div className="rating-bars">
        {Array.from({ length: max }, (_, i) => {
          const index = i + 1;
          const isActive =
            hovered !== null ? index <= hovered : index <= rating;

          return (
            <span
              key={index}
              className={`bar ${isActive ? "active" : ""}`}
              onClick={() => handleClick(index)}
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
            />
          );
        })}
      </div>
      <span className="rating-value">{displayValue}</span>
    </div>
  );
};

export default RatingBar;
