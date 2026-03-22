import React, { useState } from "react";

interface SquareRatingProps {
  max?: number;
  initial?: number;
}

const SquareRating: React.FC<SquareRatingProps> = ({
  max = 5,
  initial = 1,
}) => {
  const [rating, setRating] = useState<number>(initial);
  const [hovered, setHovered] = useState<number | null>(null);

  const handleClick = (value: number) => {
    setRating(value);
  };

  return (
    <div className="square-rating-wrapper">
      {Array.from({ length: max }, (_, i) => {
        const index = i + 1;
        const isSelected = index <= rating;
        const isHovered = hovered !== null && index <= hovered;

        const shouldHighlight = isHovered || isSelected;

        return (
          <div
            key={index}
            className={`square ${shouldHighlight ? "active-border" : ""}`}
            onClick={() => handleClick(index)}
            onMouseEnter={() => setHovered(index)}
            onMouseLeave={() => setHovered(null)}
          >
            {index}
          </div>
        );
      })}
    </div>
  );
};

export default SquareRating;
