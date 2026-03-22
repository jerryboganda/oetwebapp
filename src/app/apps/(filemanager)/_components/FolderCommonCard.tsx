import React, { useState } from "react";
import {
  Card,
  CardBody,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import { IconDotsVertical, IconEdit, IconTrash } from "@tabler/icons-react";
import { Star } from "phosphor-react";

interface FolderCommonCardProps {
  title: string;
  used: string;
  total: string;
  icon: string;
  starred?: boolean;
  onStar?: (starred: boolean) => void;
  onRename?: () => void;
  onDelete?: () => void;
}

const FolderCommonCard: React.FC<FolderCommonCardProps> = ({
  title,
  used,
  total,
  icon,
  starred = false,
  onStar,
  onRename,
  onDelete,
}) => {
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const toggleDropdown = () => setDropdownOpen((prev) => !prev);

  const showDropdown = onRename || onDelete;

  // const [starred, setStarred] = useState(false);

  const toggleStar = () => {
    if (onStar) {
      onStar(!starred);
    }
  };

  return (
    <Card>
      <CardBody className="folder-card position-relative">
        <div
          className="position-absolute top-0 start-0 m-2"
          onClick={toggleStar}
        >
          <Star
            size={18}
            weight={starred ? "fill" : "regular"}
            className="text-warning"
          />
        </div>

        {/* Dropdown (conditionally rendered) */}
        {showDropdown && (
          <div className="folder-dropdown position-absolute top-0 end-0 p-2">
            <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
              <DropdownToggle
                tag="a"
                role="button"
                className="p-0"
                onClick={(e) => e.preventDefault()}
              >
                <IconDotsVertical size={18} />
              </DropdownToggle>
              <DropdownMenu end>
                {onRename && (
                  <DropdownItem onClick={onRename}>
                    <IconEdit size={18} className="text-success me-2" />
                    Rename
                  </DropdownItem>
                )}
                {onDelete && (
                  <DropdownItem onClick={onDelete}>
                    <IconTrash size={18} className="text-danger me-2" />
                    Delete
                  </DropdownItem>
                )}
              </DropdownMenu>
            </Dropdown>
          </div>
        )}

        {/* Folder image and title */}
        <div className="fileimage text-center my-4">
          <img src={icon} alt={title} className="img-fluid" />
          <p className="mb-0 fs-6 fw-semibold mt-2">{title}</p>
        </div>

        {/* Storage Info */}
        <div className="d-flex justify-content-between mt-2">
          <p className="text-secondary mb-0 fw-medium">{used}</p>
          <p className="text-secondary mb-0 fw-medium">{total}</p>
        </div>
      </CardBody>
    </Card>
  );
};

export default FolderCommonCard;
