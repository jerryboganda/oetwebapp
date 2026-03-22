import * as React from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemText from "@mui/material/ListItemText";
import Grid from "@mui/material/Grid";
import TextField from "@mui/material/TextField";

interface TransferListBoxProps {
  leftItems: string[];
  rightItems: string[];
  setRightItems: React.Dispatch<React.SetStateAction<string[]>>;
  leftTitle?: string;
  rightTitle?: string;
  addAllText?: string;
  addText?: string;
  removeText?: string;
  removeAllText?: string;
}

const not = (a: readonly string[], b: readonly string[]) =>
  a.filter((value) => b.indexOf(value) === -1);

export default function TransferListBox({
  leftItems,
  rightItems,
  setRightItems,
  leftTitle = "Available options",
  rightTitle = "Selected options",
  addAllText,
  addText,
  removeText,
  removeAllText,
}: TransferListBoxProps) {
  const [selectedLeft, setSelectedLeft] = React.useState<string[]>([]);
  const [selectedRight, setSelectedRight] = React.useState<string[]>([]);
  const [leftSearch, setLeftSearch] = React.useState("");
  const [rightSearch, setRightSearch] = React.useState("");

  const handleToggleLeft = (value: string) => {
    setSelectedLeft((prev) =>
      prev.includes(value) ? prev.filter((i) => i !== value) : [...prev, value]
    );
  };

  const handleToggleRight = (value: string) => {
    setSelectedRight((prev) =>
      prev.includes(value) ? prev.filter((i) => i !== value) : [...prev, value]
    );
  };

  const handleAllRight = () => setRightItems([...rightItems, ...leftItems]);
  const handleCheckedRight = () => {
    setRightItems([...rightItems, ...selectedLeft]);
    setSelectedLeft([]);
  };
  const handleCheckedLeft = () => {
    setRightItems(not(rightItems, selectedRight));
    setSelectedRight([]);
  };
  const handleAllLeft = () => setRightItems([]);

  const moveToRight = (item: string) => {
    if (!rightItems.includes(item)) setRightItems([...rightItems, item]);
  };

  const moveToLeft = (item: string) => {
    setRightItems(not(rightItems, [item]));
  };

  const renderList = (
    items: string[],
    selected: string[],
    onToggle: (v: string) => void,
    searchValue: string,
    setSearchValue: React.Dispatch<React.SetStateAction<string>>,
    title: string
  ) => {
    const filteredItems = items.filter((i) =>
      i.toLowerCase().includes(searchValue.toLowerCase())
    );

    const doubleClickHandler =
      !addText && !addAllText && !removeText && !removeAllText
        ? title === leftTitle
          ? moveToRight
          : moveToLeft
        : onToggle;

    return (
      <Grid container direction="column" sx={{ width: "100%" }} spacing={1}>
        <Grid sx={{ width: "100%" }}>
          <Box sx={{ fontWeight: 500, mb: 0.5 }}>{title}</Box>
          <TextField
            fullWidth
            size="small"
            placeholder="Search..."
            value={searchValue}
            onChange={(e) => setSearchValue(e.target.value)}
          />
        </Grid>

        <Grid sx={{ width: "250px" }}>
          <Paper
            sx={{
              width: "100%",
              height: 200,
              borderRadius: 2,
              overflowY: "scroll",
              border: "1px solid #ccc",
            }}
          >
            <List dense>
              {filteredItems.map((value, index) => (
                <ListItem
                  key={`${value}-${index}`}
                  component={"button" as React.ElementType}
                  onClick={() => onToggle(value)}
                  onDoubleClick={() => doubleClickHandler(value)}
                  sx={{
                    width: "100%",
                    bgcolor: selected.includes(value)
                      ? "#8c76f0e6"
                      : "background.paper",
                    color: selected.includes(value) ? "white" : "text.primary",
                    border: "none",
                    cursor: "pointer",
                  }}
                >
                  <ListItemText primary={value} />
                </ListItem>
              ))}
            </List>
          </Paper>
        </Grid>
      </Grid>
    );
  };

  return (
    <Box sx={{ width: "100%", mt: 2 }}>
      <Grid
        container
        spacing={2}
        justifyContent="space-between"
        alignItems="center"
      >
        <Grid>
          {renderList(
            leftItems.filter((i) => !rightItems.includes(i)),
            selectedLeft,
            handleToggleLeft,
            leftSearch,
            setLeftSearch,
            leftTitle
          )}
        </Grid>

        {(addText || addAllText || removeText || removeAllText) && (
          <Grid>
            <Grid container direction="column" alignItems="center" my={5}>
              {addAllText && (
                <Button
                  sx={{ my: 0.5, width: 120, backgroundColor: "#8c76f0e6" }}
                  variant="contained"
                  size="small"
                  onClick={handleAllRight}
                >
                  {addAllText}
                </Button>
              )}
              {addText && (
                <Button
                  sx={{ my: 0.5, width: 120, backgroundColor: "#8c76f0e6" }}
                  variant="contained"
                  size="small"
                  onClick={handleCheckedRight}
                >
                  {addText}
                </Button>
              )}
              {removeText && (
                <Button
                  sx={{ my: 0.5, width: 120, backgroundColor: "#8c76f0e6" }}
                  variant="contained"
                  size="small"
                  onClick={handleCheckedLeft}
                >
                  {removeText}
                </Button>
              )}
              {removeAllText && (
                <Button
                  sx={{ my: 0.5, width: 120, backgroundColor: "#8c76f0e6" }}
                  variant="contained"
                  size="small"
                  onClick={handleAllLeft}
                >
                  {removeAllText}
                </Button>
              )}
            </Grid>
          </Grid>
        )}

        <Grid>
          {renderList(
            rightItems,
            selectedRight,
            handleToggleRight,
            rightSearch,
            setRightSearch,
            rightTitle
          )}
        </Grid>
      </Grid>
    </Box>
  );
}
